﻿using HandheldCompanion.ADLX;
using HandheldCompanion.GraphicsProcessingUnit;
using HandheldCompanion.IGCL;
using HandheldCompanion.Managers.Desktop;
using HandheldCompanion.Misc;
using HandheldCompanion.Shared;
using SharpDX.Direct3D9;
using System.Collections.Concurrent;
using System.Linq;

namespace HandheldCompanion.Managers
{
    public static class GPUManager
    {
        #region events
        public static event InitializedEventHandler? Initialized;
        public delegate void InitializedEventHandler(bool HasIGCL, bool HasADLX);

        public static event HookedEventHandler? Hooked;
        public delegate void HookedEventHandler(GPU GPU);

        public static event UnhookedEventHandler? Unhooked;
        public delegate void UnhookedEventHandler(GPU GPU);
        #endregion

        public static bool IsInitialized = false;
        public static bool IsLoaded_IGCL = false;
        public static bool IsLoaded_ADLX = false;

        private static GPU currentGPU = null;
        private static ConcurrentDictionary<AdapterInformation, GPU> DisplayGPU = new();

        public static void Start()
        {
            if (IsInitialized)
                return;

            if (!IsLoaded_IGCL && GPU.HasIntelGPU())
            {
                // try to initialized IGCL
                IsLoaded_IGCL = IGCLBackend.Initialize();

                if (IsLoaded_IGCL)
                    LogManager.LogInformation("IGCL was successfully initialized", "GPUManager");
                else
                    LogManager.LogError("Failed to initialize IGCL", "GPUManager");
            }

            if (!IsLoaded_ADLX && GPU.HasAMDGPU())
            {
                // try to initialized ADLX
                IsLoaded_ADLX = ADLXBackend.SafeIntializeAdlx();

                if (IsLoaded_ADLX)
                    LogManager.LogInformation("ADLX {0} was successfully initialized", ADLXBackend.GetVersion(), "GPUManager");
                else
                    LogManager.LogError("Failed to initialize ADLX", "GPUManager");
            }

            // todo: check if usefull on resume
            // it could be DeviceManager_DisplayAdapterArrived is called already, making this redundant
            currentGPU?.Start();

            // manage events
            ProfileManager.Applied += ProfileManager_Applied;
            ProfileManager.Discarded += ProfileManager_Discarded;
            ProfileManager.Updated += ProfileManager_Updated;
            ManagerFactory.deviceManager.DisplayAdapterArrived += DeviceManager_DisplayAdapterArrived;
            ManagerFactory.deviceManager.DisplayAdapterRemoved += DeviceManager_DisplayAdapterRemoved;
            ManagerFactory.multimediaManager.PrimaryScreenChanged += MultimediaManager_PrimaryScreenChanged;

            // raise events
            if (ProfileManager.IsInitialized)
            {
                ProfileManager_Applied(ProfileManager.GetCurrent(), UpdateSource.Background);
            }

            switch (ManagerFactory.deviceManager.Status)
            {
                default:
                case ManagerStatus.Initializing:
                    ManagerFactory.deviceManager.Initialized += DeviceManager_Initialized;
                    break;
                case ManagerStatus.Initialized:
                    QueryDevices();
                    break;
            }

            IsInitialized = true;
            Initialized?.Invoke(IsLoaded_IGCL, IsLoaded_ADLX);

            LogManager.LogInformation("{0} has started", "GPUManager");
        }

        public static void Stop()
        {
            if (!IsInitialized)
                return;

            // manage events
            ProfileManager.Applied -= ProfileManager_Applied;
            ProfileManager.Discarded -= ProfileManager_Discarded;
            ProfileManager.Updated -= ProfileManager_Updated;
            ManagerFactory.deviceManager.DisplayAdapterArrived -= DeviceManager_DisplayAdapterArrived;
            ManagerFactory.deviceManager.DisplayAdapterRemoved -= DeviceManager_DisplayAdapterRemoved;
            ManagerFactory.deviceManager.Initialized -= DeviceManager_Initialized;
            ManagerFactory.multimediaManager.PrimaryScreenChanged -= MultimediaManager_PrimaryScreenChanged;

            foreach (GPU gpu in DisplayGPU.Values)
                gpu.Stop();

            lock (GPU.functionLock)
            {
                if (IsLoaded_IGCL)
                {
                    IGCLBackend.Terminate();
                    IsLoaded_IGCL = false;
                }

                if (IsLoaded_ADLX)
                {
                    ADLXBackend.CloseAdlx();
                    IsLoaded_ADLX = false;
                }
            }

            IsInitialized = false;

            LogManager.LogInformation("{0} has stopped", "GPUManager");
        }

        private static void DeviceManager_Initialized()
        {
            QueryDevices();
        }

        private static void QueryDevices()
        {
            foreach (AdapterInformation displayAdapter in ManagerFactory.deviceManager.displayAdapters.Values)
                DeviceManager_DisplayAdapterArrived(displayAdapter);
        }

        private static void GPUConnect(GPU GPU)
        {
            LogManager.LogInformation("Connecting DisplayAdapter {0}", GPU.ToString());

            // update current GPU
            currentGPU = GPU;

            GPU.ImageSharpeningChanged += CurrentGPU_ImageSharpeningChanged;
            GPU.GPUScalingChanged += CurrentGPU_GPUScalingChanged;
            GPU.IntegerScalingChanged += CurrentGPU_IntegerScalingChanged;

            if (GPU is AMDGPU)
            {
                ((AMDGPU)GPU).RSRStateChanged += CurrentGPU_RSRStateChanged;
                ((AMDGPU)GPU).AFMFStateChanged += CurrentGPU_AFMFStateChanged;
            }
            else if (GPU is IntelGPU)
            {
                // do something
            }

            if (GPU.IsInitialized)
            {
                GPU.Start();
                Hooked?.Invoke(GPU);

                LogManager.LogInformation("Hooked DisplayAdapter: {0}", GPU.ToString());
            }
        }

        private static void GPUDisconnect(GPU GPU)
        {
            LogManager.LogInformation("Disconnecting DisplayAdapter {0}", GPU.ToString());

            if (currentGPU == GPU)
                Unhooked?.Invoke(GPU);

            GPU.ImageSharpeningChanged -= CurrentGPU_ImageSharpeningChanged;
            GPU.GPUScalingChanged -= CurrentGPU_GPUScalingChanged;
            GPU.IntegerScalingChanged -= CurrentGPU_IntegerScalingChanged;

            if (GPU is AMDGPU)
            {
                ((AMDGPU)GPU).RSRStateChanged -= CurrentGPU_RSRStateChanged;
                ((AMDGPU)GPU).AFMFStateChanged -= CurrentGPU_AFMFStateChanged;
            }
            else if (GPU is IntelGPU)
            {
                // do something
            }

            GPU.Stop();
        }

        private static void DeviceManager_DisplayAdapterArrived(AdapterInformation adapterInformation)
        {
            // GPU is already part of the dictionary
            if (DisplayGPU.ContainsKey(adapterInformation))
                return;

            GPU newGPU = null;

            if ((adapterInformation.Details.Description.Contains("Advanced Micro Devices") || adapterInformation.Details.Description.Contains("AMD")) && IsLoaded_ADLX)
            {
                newGPU = new AMDGPU(adapterInformation);
            }
            else if (adapterInformation.Details.Description.Contains("Intel") && IsLoaded_IGCL)
            {
                newGPU = new IntelGPU(adapterInformation);
            }

            if (newGPU is null)
            {
                LogManager.LogError("Unsupported DisplayAdapter: {0}, VendorID:{1}, DeviceId:{2}", adapterInformation.Details.Description, adapterInformation.Details.VendorId, adapterInformation.Details.DeviceId);
                return;
            }

            if (!newGPU.IsInitialized)
            {
                LogManager.LogError("Failed to initialize DisplayAdapter: {0}, VendorID:{1}, DeviceId:{2}", adapterInformation.Details.Description, adapterInformation.Details.VendorId, adapterInformation.Details.DeviceId);
                return;
            }

            LogManager.LogInformation("Detected DisplayAdapter: {0}, VendorID:{1}, DeviceId:{2}", adapterInformation.Details.Description, adapterInformation.Details.VendorId, adapterInformation.Details.DeviceId);

            // Add to dictionary
            DisplayGPU.TryAdd(adapterInformation, newGPU);

            // Force send an update
            if (ManagerFactory.multimediaManager.PrimaryDesktop != null)
                MultimediaManager_PrimaryScreenChanged(ManagerFactory.multimediaManager.PrimaryDesktop);
        }

        private static void MultimediaManager_PrimaryScreenChanged(DesktopScreen screen)
        {
            AdapterInformation key = DisplayGPU.Keys.FirstOrDefault(GPU => GPU.Details.DeviceName == screen.screen.DeviceName);
            if (key is not null && DisplayGPU.TryGetValue(key, out GPU gpu))
            {
                LogManager.LogInformation("Retrieved DisplayAdapter: {0} for screen: {1}", gpu.ToString(), screen.ToString());

                if (currentGPU != gpu)
                {
                    // Disconnect from the current GPU, if any
                    if (currentGPU is not null)
                        GPUDisconnect(currentGPU);

                    // Connect to the new GPU
                    GPUConnect(gpu);
                }
            }
            else
            {
                LogManager.LogError("Failed to retrieve DisplayAdapter for screen: {0}", screen.ToString());
            }
        }

        private static void DeviceManager_DisplayAdapterRemoved(AdapterInformation adapterInformation)
        {
            if (DisplayGPU.TryRemove(adapterInformation, out GPU gpu))
            {
                GPUDisconnect(gpu);
                gpu.Dispose();
            }
        }

        public static GPU GetCurrent()
        {
            return currentGPU;
        }

        private static void CurrentGPU_RSRStateChanged(bool Supported, bool Enabled, int Sharpness)
        {
            if (!IsInitialized)
                return;

            // todo: use ProfileMager events
            Profile profile = ProfileManager.GetCurrent();
            AMDGPU amdGPU = (AMDGPU)currentGPU;

            if (Enabled != profile.RSREnabled)
                amdGPU.SetRSR(profile.RSREnabled);
            if (Sharpness != profile.RSRSharpness)
                amdGPU.SetRSRSharpness(profile.RSRSharpness);
        }

        private static void CurrentGPU_AFMFStateChanged(bool Supported, bool Enabled)
        {
            if (!IsInitialized)
                return;

            // todo: use ProfileMager events
            Profile profile = ProfileManager.GetCurrent();
            AMDGPU amdGPU = (AMDGPU)currentGPU;

            if (Enabled != profile.AFMFEnabled)
                amdGPU.SetAFMF(profile.AFMFEnabled);
        }

        private static void CurrentGPU_IntegerScalingChanged(bool Supported, bool Enabled)
        {
            if (!IsInitialized)
                return;

            // todo: use ProfileMager events
            Profile profile = ProfileManager.GetCurrent();

            if (Enabled != profile.IntegerScalingEnabled)
                currentGPU.SetIntegerScaling(profile.IntegerScalingEnabled, profile.IntegerScalingType);
        }

        private static void CurrentGPU_GPUScalingChanged(bool Supported, bool Enabled, int Mode)
        {
            if (!IsInitialized)
                return;

            // todo: use ProfileMager events
            Profile profile = ProfileManager.GetCurrent();

            if (Enabled != profile.GPUScaling)
                currentGPU.SetGPUScaling(profile.GPUScaling);
            if (Mode != profile.ScalingMode)
                currentGPU.SetScalingMode(profile.ScalingMode);
        }

        private static void CurrentGPU_ImageSharpeningChanged(bool Enabled, int Sharpness)
        {
            if (!IsInitialized)
                return;

            // todo: use ProfileMager events
            Profile profile = ProfileManager.GetCurrent();

            if (Enabled != profile.RISEnabled)
                currentGPU.SetImageSharpening(profile.RISEnabled);
            if (Sharpness != profile.RISSharpness)
                currentGPU.SetImageSharpeningSharpness(Sharpness);
        }

        private static void ProfileManager_Applied(Profile profile, UpdateSource source)
        {
            if (!IsInitialized || currentGPU is null)
                return;

            try
            {
                // apply profile GPU Scaling
                // apply profile scaling mode
                if (profile.GPUScaling)
                {
                    if (!currentGPU.GetGPUScaling())
                        currentGPU.SetGPUScaling(true);

                    if (currentGPU.GetScalingMode() != profile.ScalingMode)
                        currentGPU.SetScalingMode(profile.ScalingMode);
                }
                else if (currentGPU.GetGPUScaling())
                {
                    currentGPU.SetGPUScaling(false);
                }

                // apply profile RSR / AFMF
                if (currentGPU is AMDGPU amdGPU)
                {
                    if (profile.RSREnabled)
                    {
                        if (!amdGPU.GetRSR())
                            amdGPU.SetRSR(true);

                        if (amdGPU.GetRSRSharpness() != profile.RSRSharpness)
                            amdGPU.SetRSRSharpness(profile.RSRSharpness);
                    }
                    else if (amdGPU.GetRSR())
                    {
                        amdGPU.SetRSR(false);
                    }

                    if (profile.AFMFEnabled)
                    {
                        if (!amdGPU.GetAFMF())
                            amdGPU.SetAFMF(true);

                        if (!amdGPU.GetAntiLag())
                            amdGPU.SetAntiLag(true);
                    }
                    else if (amdGPU.GetAFMF())
                    {
                        amdGPU.SetAFMF(false);
                    }
                }

                // apply profile Integer Scaling
                if (profile.IntegerScalingEnabled)
                {
                    if (!currentGPU.GetIntegerScaling())
                        currentGPU.SetIntegerScaling(true, profile.IntegerScalingType);
                }
                else if (currentGPU.GetIntegerScaling())
                {
                    currentGPU.SetIntegerScaling(false, 0);
                }

                // apply profile image sharpening
                if (profile.RISEnabled)
                {
                    if (!currentGPU.GetImageSharpening())
                        currentGPU.SetImageSharpening(profile.RISEnabled);

                    if (currentGPU.GetImageSharpeningSharpness() != profile.RISSharpness)
                        currentGPU.SetImageSharpeningSharpness(profile.RISSharpness);
                }
                else if (currentGPU.GetImageSharpening())
                {
                    currentGPU.SetImageSharpening(false);
                }
            }
            catch { }
        }

        private static void ProfileManager_Discarded(Profile profile, bool swapped)
        {
            if (!IsInitialized || currentGPU is null)
                return;

            // don't bother discarding settings, new one will be enforce shortly
            if (swapped)
                return;

            try
            {
                /*
                // restore default GPU Scaling
                if (profile.GPUScaling && currentGPU.GetGPUScaling())
                    currentGPU.SetGPUScaling(false);
                */

                // restore default RSR
                if (currentGPU is AMDGPU amdGPU)
                {
                    if (profile.RSREnabled && amdGPU.GetRSR())
                        amdGPU.SetRSR(false);
                }

                // restore default integer scaling
                if (profile.IntegerScalingEnabled && currentGPU.GetIntegerScaling())
                    currentGPU.SetIntegerScaling(false, 0);

                // restore default image sharpening
                if (profile.RISEnabled && currentGPU.GetImageSharpening())
                    currentGPU.SetImageSharpening(false);
            }
            catch { }
        }

        // todo: moveme
        private static void ProfileManager_Updated(Profile profile, UpdateSource source, bool isCurrent)
        {
            ProcessEx.SetAppCompatFlag(profile.Path, ProcessEx.DisabledMaximizedWindowedValue, !profile.FullScreenOptimization);
            ProcessEx.SetAppCompatFlag(profile.Path, ProcessEx.HighDPIAwareValue, !profile.HighDPIAware);
        }
    }
}
