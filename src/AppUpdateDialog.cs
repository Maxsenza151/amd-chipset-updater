using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ChipsetUpdater
{
    enum AppUpdateChoice { Later, UpdateNow, Skip }

    /// <summary>"A new version is available" popup shown when the app opens.</summary>
    sealed class AppUpdateDialog : Form
    {
        public AppUpdateChoice Choice { get; private set; }

        readonly float scale;

        public AppUpdateDialog(AppRelease release, Version current)
        {
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            Choice = AppUpdateChoice.Later;

            Text = "Update available";
            Icon = AppIcon.Get();
            Font = new Font("Segoe UI", 9f);
            BackColor = SystemColors.Window;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(S(18), S(14), S(18), S(12));

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };

            root.Controls.Add(new Label
            {
                Text = "A new version of " + Program.AppName + " is available",
                Font = new Font("Segoe UI Semibold", 11f),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, S(6)),
            });
            root.Controls.Add(new Label
            {
                Text = "Version " + release.VersionText + " is available. You have version " + VersionUtil.Short(current) + ".",
                AutoSize = true,
                MaximumSize = new Size(S(420), 0),
            });

            var changelog = new LinkLabel { Text = "See changelog", AutoSize = true, Margin = new Padding(0, S(6), 0, 0) };
            changelog.LinkClicked += delegate
            {
                try { Process.Start(new ProcessStartInfo(release.PageUrl) { UseShellExecute = true }); }
                catch (Exception) { }
            };
            root.Controls.Add(changelog);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, S(18), 0, 0),
                WrapContents = false,
            };
            Button update = MakeButton("Update now", AppUpdateChoice.UpdateNow);
            Button later = MakeButton("Later", AppUpdateChoice.Later);
            Button skip = MakeButton("Skip this version", AppUpdateChoice.Skip);
            buttons.Controls.Add(update);
            buttons.Controls.Add(later);
            buttons.Controls.Add(skip);
            root.Controls.Add(buttons);

            Controls.Add(root);
            AcceptButton = update;
            CancelButton = later;
        }

        Button MakeButton(string text, AppUpdateChoice choice)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(S(88), S(28)),
                Margin = new Padding(S(8), 0, 0, 0),
                UseVisualStyleBackColor = true,
            };
            b.Click += delegate
            {
                Choice = choice;
                Close();
            };
            return b;
        }

        int S(int px) { return (int)Math.Round(px * scale); }
    }
}
