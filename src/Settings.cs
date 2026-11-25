using System;
using Microsoft.Win32;

namespace ChipsetUpdater
{
    /// <summary>User settings in HKCU\Software\AMDChipsetUpdater.</summary>
    static class Settings
    {
        const string KeyPath = @"Software\AMDChipsetUpdater";

        /// <summary>Socket chosen by the user, or null for automatic detection.</summary>
        public static string ManualSocket { get { return Get("ManualSocket"); } }

        /// <summary>Chipset chosen by the user (may be null when only the socket was chosen).</summary>
        public static string ManualChipset { get { return Get("ManualChipset"); } }

        public static void SetManualPlatform(string socket, string chipset)
        {
            Set("ManualSocket", socket);
            Set("ManualChipset", chipset);
        }

        public static void ClearManualPlatform()
        {
            Set("ManualSocket", null);
            Set("ManualChipset", null);
        }

        /// <summary>App version the user chose to skip in the update popup (e.g. "1.2.0").</summary>
        public static string SkippedAppVersion
        {
            get { return Get("SkippedAppVersion"); }
            set { Set("SkippedAppVersion", value); }
        }

        static string Get(string name)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    string v = k == null ? null : k.GetValue(name) as string;
                    return string.IsNullOrEmpty(v) ? null : v;
                }
            }
            catch (Exception) { return null; }
        }

        static void Set(string name, string value)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (string.IsNullOrEmpty(value)) k.DeleteValue(name, false);
                else k.SetValue(name, value, RegistryValueKind.String);
            }
        }
    }
}
