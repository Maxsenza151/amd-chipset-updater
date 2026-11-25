using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ChipsetUpdater
{
    /// <summary>
    /// Startup mode (--check): no window. Checks once, shows a Windows notification
    /// if there is something to install, and exits.
    /// </summary>
    static class BackgroundCheck
    {
        const int Retries = 3;                     // the network may not be ready right after logon
        const int RetryDelayMs = 30000;
        const int NotificationLifetimeMs = 60000;  // how long a click on the notification still opens the app

        public static int Run(bool forceNotify)
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch (Exception) { }

            CheckResult r = UpdateChecker.Check();
            for (int i = 0; i < Retries && r.Status == UpdateStatus.Error; i++)
            {
                Thread.Sleep(RetryDelayMs);
                r = UpdateChecker.Check();
            }

            // An installed version that cannot be read is shown in the window, but not notified at every logon
            // (reinstalling would not change it).
            bool versionUnreadable = r.Installed != null && r.Installed.Version == null;
            bool hasUpdate = (r.Status == UpdateStatus.UpdateAvailable && !versionUnreadable)
                             || r.Status == UpdateStatus.NotInstalled;
            if (!hasUpdate && !forceNotify)
                return r.Status == UpdateStatus.Error ? 2 : 0;

            Notify(r);
            return 1;
        }

        static void Notify(CheckResult r)
        {
            string title, text;
            string latest = r.Latest != null ? r.Latest.VersionText : "?";
            if (r.Status == UpdateStatus.NotInstalled)
            {
                title = "AMD chipset driver not installed";
                text = "Version " + latest + " is available for this PC. Click to install it.";
            }
            else if (r.Status == UpdateStatus.UpdateAvailable)
            {
                title = "AMD chipset driver: update available";
                text = "Version " + latest + " is available (installed: "
                     + (r.Installed != null ? r.Installed.VersionText : "unknown") + "). Click to update.";
            }
            else
            {
                title = "AMD Chipset Updater";
                text = r.Message ?? "Test notification.";
            }

            Application.EnableVisualStyles();
            using (var icon = new NotifyIcon())
            using (var timer = new System.Windows.Forms.Timer { Interval = NotificationLifetimeMs })
            {
                icon.Icon = AppIcon.Get();
                icon.Text = Program.AppName;
                icon.Visible = true;

                EventHandler open = delegate
                {
                    try { Process.Start(Application.ExecutablePath); } catch (Exception) { }
                    Application.ExitThread();
                };
                icon.BalloonTipClicked += open;
                icon.Click += open;
                timer.Tick += delegate { Application.ExitThread(); };

                timer.Start();
                icon.ShowBalloonTip(10000, title, text, ToolTipIcon.Info);
                Application.Run();
                icon.Visible = false;
            }
        }
    }

    static class AppIcon
    {
        public static Icon Get()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
            catch (Exception) { return SystemIcons.Application; }
        }
    }
}
