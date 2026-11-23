using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ChipsetUpdater
{
    /// <summary>Per-user "run at Windows startup" entry. The installer writes the same value.</summary>
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "AMDChipsetUpdater";

        static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" --check"; }
        }

        public static bool IsEnabled()
        {
            return RegisteredCommand() != null;
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue(ValueName, Command, RegistryValueKind.String);
                else k.DeleteValue(ValueName, false);
            }
        }

        /// <summary>If the registered exe no longer exists (portable copy moved), point it here.</summary>
        public static void RepairIfMoved()
        {
            string cmd = RegisteredCommand();
            if (cmd == null) return;
            Match m = Regex.Match(cmd, "^\"([^\"]+)\"");
            if (m.Success && !File.Exists(m.Groups[1].Value)) SetEnabled(true);
        }

        static string RegisteredCommand()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k == null ? null : k.GetValue(ValueName) as string;
        }
    }
}
