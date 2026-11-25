using System;

namespace ChipsetUpdater
{
    enum UpdateStatus { UpToDate, UpdateAvailable, NotInstalled, Unsupported, Error }

    sealed class CheckResult
    {
        public HardwareInfo Hardware;
        public InstalledDriver Installed;
        public DriverRelease Latest;
        public UpdateStatus Status;
        public string Message;
    }

    /// <summary>Detects the hardware, reads the installed version and compares it with amd.com.</summary>
    static class UpdateChecker
    {
        public static CheckResult Check()
        {
            var r = new CheckResult();
            try
            {
                r.Hardware = HardwareDetector.Detect();
                if (Settings.ManualSocket != null)
                    r.Hardware.ApplyManual(Settings.ManualSocket, Settings.ManualChipset);
            }
            catch (Exception ex)
            {
                return Fail(r, "Could not read the hardware information: " + ex.Message);
            }

            try { r.Installed = InstalledDriver.Find(); }
            catch (Exception) { r.Installed = null; }

            if (!r.Hardware.IsSupported)
            {
                r.Status = UpdateStatus.Unsupported;
                r.Message = r.Hardware.UnsupportedReason;
                return r;
            }

            try
            {
                r.Latest = AmdDriverSource.GetLatest(r.Hardware);
            }
            catch (Exception ex)
            {
                return Fail(r, "Could not get the latest version from amd.com: " + ex.Message);
            }

            if (r.Installed == null)
            {
                r.Status = UpdateStatus.NotInstalled;
                r.Message = "The AMD chipset driver is not installed. Version " + r.Latest.VersionText + " is available.";
            }
            else if (r.Installed.Version == null)
            {
                r.Status = UpdateStatus.UpdateAvailable;
                r.Message = "The installed version could not be read. Version " + r.Latest.VersionText + " is available.";
            }
            else if (r.Installed.Version < r.Latest.Version)
            {
                r.Status = UpdateStatus.UpdateAvailable;
                r.Message = "Version " + r.Latest.VersionText + " is available.";
            }
            else
            {
                r.Status = UpdateStatus.UpToDate;
                r.Message = "You have the latest AMD chipset driver.";
            }
            return r;
        }

        static CheckResult Fail(CheckResult r, string message)
        {
            r.Status = UpdateStatus.Error;
            r.Message = message;
            return r;
        }
    }
}
