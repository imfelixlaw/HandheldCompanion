﻿using System.Collections.Generic;
using System.Numerics;
using ControllerCommon.Inputs;
using ControllerCommon.Managers;
using WindowsInput.Events;

namespace ControllerCommon.Devices;

public class AOKZOEA1Pro : IDevice
{
    public AOKZOEA1Pro()
    {
        // device specific settings
        ProductIllustration = "device_aokzoe_a1";
        ProductModel = "AOKZOEA1Pro";

        // https://www.amd.com/en/products/apu/amd-ryzen-7-7840u
        nTDP = new double[] { 15, 15, 20 };
        cTDP = new double[] { 4, 28 };
        GfxClock = new double[] { 100, 2700 };

        AngularVelocityAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'X' },
            { 'Y', 'Z' },
            { 'Z', 'Y' }
        };

        AccelerationAxis = new Vector3(-1.0f, 1.0f, -1.0f);
        AccelerationAxisSwap = new SortedDictionary<char, char>
        {
            { 'X', 'X' },
            { 'Y', 'Z' },
            { 'Z', 'Y' }
        };

        // device specific capacities
        Capacities = DeviceCapacities.FanControl;

        ECDetails = new ECDetails
        {
            AddressControl = 0x44A,
            AddressDuty = 0x44B,
            AddressRegistry = 0x4E,
            AddressData = 0x4F,
            ValueMin = 0,
            ValueMax = 184
        };

        /*
        // Commented out, keystrokes are not
        // consistent when HC is running

        // Home
        OEMChords.Add(new DeviceChord("Home",
            new List<KeyCode> { KeyCode.LWin, KeyCode.D },
            new List<KeyCode> { KeyCode.LWin, KeyCode.D },
            false, ButtonFlags.OEM1
        ));

        // Keyboard
        OEMChords.Add(new DeviceChord("Keyboard",
            new List<KeyCode> { KeyCode.RControlKey, KeyCode.LWin, KeyCode.O },
            new List<KeyCode> { KeyCode.O, KeyCode.LWin, KeyCode.RControlKey },
            false, ButtonFlags.OEM2
        ));

        // Turbo
        OEMChords.Add(new DeviceChord("Turbo",
            new List<KeyCode> { KeyCode.LControl, KeyCode.LWin, KeyCode.LMenu },
            new List<KeyCode> { KeyCode.LControl, KeyCode.LWin, KeyCode.LMenu },
            false, ButtonFlags.OEM3
        ));
        */

    }
    public override bool Open()
    {
        var success = base.Open();
        if (!success)
            return false;

        // allow AOKZOE A1 Pro button to pass key inputs for Turbo button
        LogManager.LogInformation("Unlocked {0} OEM button", ButtonFlags.OEM3);
        return ECRamDirectWrite(0xF1, ECDetails, 0x40);
    }

    public override void Close()
    {
        LogManager.LogInformation("Locked {0} OEM button", ButtonFlags.OEM3);
        ECRamDirectWrite(0xF1, ECDetails, 0x00);
        base.Close();
    }
}