﻿using HandheldCompanion.Controllers;
using HandheldCompanion.Managers;
using Nefarius.Utilities.DeviceManagement.PnP;
using System;

namespace HandheldCompanion.ViewModels
{
    public abstract class ILayoutPageViewModel : BaseViewModel
    {
        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (value != _isEnabled)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        public ILayoutPageViewModel()
        {
            // manage events
            ControllerManager.ControllerSelected += UpdateController;

            // send events
            if (ControllerManager.IsInitialized)
            {
                UpdateController(ControllerManager.GetTargetController());
            }
        }

        public override void Dispose()
        {
            ControllerManager.ControllerSelected -= UpdateController;
            base.Dispose();
        }

        protected abstract void UpdateController(IController controller);
    }
}
