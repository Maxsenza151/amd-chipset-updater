using System;
using Microsoft.Win32;

namespace ChipsetUpdater
{
    /// <summary>The AMD chipset package currently installed, read from the Uninstall registry keys.</summary>
    sealed class InstalledDriver
    {
        public string DisplayName;
        public string VersionText;
        public Version Version;

        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static InstalledDriver Find()
        {
            InstalledDriver best = null;
            int bestScore = 0;

            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey uninstall = hklm.OpenSubKey(UninstallKey))
                {
                    if (uninstall == null) continue;
                    foreach (string sub in uninstall.GetSubKeyNames())
                    {
                        using (RegistryKey k = uninstall.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            string name = k.GetValue("DisplayName") as string;
                            int score = Score(name);
                            if (score <= bestScore) continue;

                            string ver = k.GetValue("DisplayVersion") as string;
                            if (string.IsNullOrEmpty(ver)) continue;

                            bestScore = score;
                            best = new InstalledDriver
                            {
                                DisplayName = name,
                                VersionText = ver.Trim(),
                                Version = VersionUtil.Parse(ver),
                            };
                        }
                    }
                }
            }
            return best;
        }

        static int Score(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (string.Equals(name.Trim(), "AMD Chipset Software", StringComparison.OrdinalIgnoreCase)) return 3;
            if (name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("Chipset Software", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (string.Equals(name.Trim(), "AMD_Chipset_Drivers", StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }
    }
}
