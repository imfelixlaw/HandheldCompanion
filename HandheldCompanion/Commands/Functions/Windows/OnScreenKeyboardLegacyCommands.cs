﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace HandheldCompanion.Commands.Functions.Windows
{
    [Serializable]
    public class OnScreenKeyboardLegacyCommands : FunctionCommands
    {
        public OnScreenKeyboardLegacyCommands()
        {
            Name = Properties.Resources.Hotkey_KeyboardLegacy;
            Description = Properties.Resources.Hotkey_KeyboardLegacyDesc;
            Glyph = "\uE765";
            OnKeyUp = true;
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp)
        {
            Task.Run(() =>
            {
                // Check if there is any existing osk.exe process
                Process? existingOskProcess = Process.GetProcessesByName("osk").FirstOrDefault();
                if (existingOskProcess != null)
                {
                    // Kill the existing osk.exe process
                    existingOskProcess.Kill();
                    existingOskProcess.WaitForExit(TimeSpan.FromSeconds(3));
                }
                else
                {
                    // Start a new osk.exe process
                    Process.Start(new ProcessStartInfo("osk.exe") { UseShellExecute = true });
                }
            });

            base.Execute(IsKeyDown, IsKeyUp);
        }

        public override object Clone()
        {
            OnScreenKeyboardLegacyCommands commands = new()
            {
                commandType = commandType,
                Name = Name,
                Description = Description,
                Glyph = Glyph,
                OnKeyUp = OnKeyUp,
                OnKeyDown = OnKeyDown
            };

            return commands;
        }
    }
}