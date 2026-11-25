using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace ChipsetUpdater
{
    static class Program
    {
        public const string AppName = "AMD Chipset Updater";

        // Lets the installer detect a running copy (Inno Setup AppMutex).
        const string RunningMutexName = "AMDChipsetUpdater.Running";
        static Mutex runningMutex;

        public static void AcquireRunningMutex()
        {
            if (runningMutex == null) runningMutex = new Mutex(false, RunningMutexName);
        }

        /// <summary>Called right before starting a self-update installer, so it does not see this app as running.</summary>
        public static void ReleaseRunningMutex()
        {
            if (runningMutex == null) return;
            runningMutex.Dispose();
            runningMutex = null;
        }

        /// <summary>
        /// No arguments: opens the main window (and offers app updates from GitHub).
        /// --check: silent startup check. Shows a notification only when an update is
        ///          available, then exits. Exit code: 0 up to date, 1 notified, 2 error.
        /// --check --force-notify: same, but always notifies (for testing).
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            AcquireRunningMutex();
            try
            {
                if (HasArg(args, "--check"))
                    return BackgroundCheck.Run(HasArg(args, "--force-notify"));

                AppUpdater.CleanupLeftovers(Application.ExecutablePath);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            finally
            {
                ReleaseRunningMutex();
            }
        }

        static bool HasArg(string[] args, string name)
        {
            foreach (string a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
