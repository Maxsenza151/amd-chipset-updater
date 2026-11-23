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

        /// <summary>
        /// No arguments: opens the main window.
        /// --check: silent startup check. Shows a notification only when an update is
        ///          available, then exits. Exit code: 0 up to date, 1 notified, 2 error.
        /// --check --force-notify: same, but always notifies (for testing).
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            using (new Mutex(false, RunningMutexName))
            {
                if (HasArg(args, "--check"))
                    return BackgroundCheck.Run(HasArg(args, "--force-notify"));

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
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
