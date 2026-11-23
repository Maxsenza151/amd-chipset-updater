using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChipsetUpdater
{
    sealed class MainForm : Form
    {
        static readonly Color Green = Color.FromArgb(22, 128, 61);
        static readonly Color Orange = Color.FromArgb(196, 92, 0);
        static readonly Color Red = Color.FromArgb(190, 30, 45);
        static readonly Color Muted = Color.FromArgb(96, 104, 112);

        readonly float scale;
        readonly Label lblCpu, lblBoard, lblPlatform, lblInstalled, lblLatest, lblStatus, lblMessage, lblNote;
        readonly LinkLabel lnkPage;
        readonly Button btnCheck, btnInstall;
        readonly CheckBox chkAutostart;
        readonly ProgressBar progress;

        CheckResult result;
        bool busy;

        public MainForm()
        {
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;

            Text = Program.AppName;
            Icon = AppIcon.Get();
            Font = new Font("Segoe UI", 9f);
            BackColor = SystemColors.Window;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(S(16), S(12), S(16), S(12));

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(400)));

            var title = new Label
            {
                Text = Program.AppName,
                Font = new Font("Segoe UI Semibold", 15f),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, S(2)),
            };
            AddWide(root, title);
            AddWide(root, MutedLabel("Finds the right AMD chipset driver for this PC and keeps it up to date."));

            AddWide(root, Section("SYSTEM"));
            lblCpu = AddRow(root, "Processor");
            lblBoard = AddRow(root, "Motherboard");
            lblPlatform = AddRow(root, "Platform");
            lnkPage = new LinkLabel { AutoSize = true, Margin = ValueMargin(), Text = "—" };
            lnkPage.LinkClicked += delegate { OpenUrl(lnkPage.Tag as string); };
            AddRow(root, "Driver page", lnkPage);

            AddWide(root, Section("CHIPSET DRIVER"));
            lblInstalled = AddRow(root, "Installed version");
            lblLatest = AddRow(root, "Latest version");
            lblStatus = AddRow(root, "Status");
            lblStatus.Font = new Font("Segoe UI Semibold", 10.5f);

            progress = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Height = S(16),
                Margin = new Padding(0, S(12), 0, S(4)),
                Visible = false,
            };
            AddWide(root, progress);

            lblMessage = new Label { AutoSize = true, MaximumSize = new Size(S(520), 0), Margin = new Padding(0, S(8), 0, 0) };
            AddWide(root, lblMessage);
            lblNote = MutedLabel("");
            lblNote.Visible = false;
            AddWide(root, lblNote);

            var bottom = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, S(14), 0, 0),
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            chkAutostart = new CheckBox
            {
                Text = "Check for updates when Windows starts",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            btnCheck = MakeButton("Check again");
            btnInstall = MakeButton("Install");
            btnInstall.Enabled = false;
            bottom.Controls.Add(chkAutostart, 0, 0);
            bottom.Controls.Add(btnCheck, 1, 0);
            bottom.Controls.Add(btnInstall, 2, 0);
            AddWide(root, bottom);

            var footer = MutedLabel("Unofficial tool, not affiliated with AMD. Drivers are downloaded from amd.com.");
            footer.Font = new Font("Segoe UI", 8f);
            footer.Margin = new Padding(0, S(12), 0, 0);
            AddWide(root, footer);

            Controls.Add(root);
            AcceptButton = btnInstall;

            try
            {
                Autostart.RepairIfMoved();
                chkAutostart.Checked = Autostart.IsEnabled();
            }
            catch (Exception) { }
            chkAutostart.CheckedChanged += OnAutostartChanged;
            btnCheck.Click += delegate { RunCheck(); };
            btnInstall.Click += delegate { RunInstall(); };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RunCheck();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && e.CloseReason == CloseReason.UserClosing &&
                MessageBox.Show(this, "An operation is still running. Close anyway?", Program.AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        // ---------------------------------------------------------------- check

        async void RunCheck()
        {
            if (busy) return;
            SetBusy(true, "Checking your hardware and amd.com…", true);
            lblStatus.Text = "Checking…";
            lblStatus.ForeColor = Muted;
            try
            {
                result = await Task.Run(() => UpdateChecker.Check());
                ShowResult(result);
            }
            finally
            {
                SetBusy(false, null, false);
            }
        }

        void ShowResult(CheckResult r)
        {
            HardwareInfo hw = r.Hardware;
            lblCpu.Text = hw != null && hw.CpuName != null ? hw.CpuName : "Unknown";
            lblBoard.Text = hw != null ? hw.BoardText : "Unknown";
            lblPlatform.Text = hw != null ? hw.PlatformText : "Unknown";

            string page = r.Latest != null ? r.Latest.PageUrl : (hw != null ? hw.DriverPageUrl : null);
            lnkPage.Tag = page;
            lnkPage.Text = page != null ? "Open on amd.com" : "—";
            lnkPage.LinkArea = page != null ? new LinkArea(0, lnkPage.Text.Length) : new LinkArea(0, 0);

            lblInstalled.Text = r.Installed != null ? r.Installed.VersionText : "Not installed";
            lblLatest.Text = r.Latest != null ? r.Latest.VersionText : "—";

            switch (r.Status)
            {
                case UpdateStatus.UpToDate:
                    SetStatus("Updated", Green, "Reinstall");
                    break;
                case UpdateStatus.UpdateAvailable:
                    SetStatus("Update available", Orange, "Update");
                    break;
                case UpdateStatus.NotInstalled:
                    SetStatus("Not installed", Orange, "Install");
                    break;
                case UpdateStatus.Unsupported:
                    SetStatus("Not supported", Muted, "Install");
                    break;
                default:
                    SetStatus("Check failed", Red, "Install");
                    break;
            }
            lblMessage.Text = r.Message ?? "";
            lblMessage.ForeColor = r.Status == UpdateStatus.Error ? Red : ForeColor;

            bool hasNote = hw != null && !string.IsNullOrEmpty(hw.Note);
            lblNote.Text = hasNote ? hw.Note : "";
            lblNote.Visible = hasNote;
        }

        void SetStatus(string text, Color color, string buttonText)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
            btnInstall.Text = buttonText;
        }

        // -------------------------------------------------------------- install

        async void RunInstall()
        {
            if (busy || result == null || result.Latest == null) return;
            DriverRelease release = result.Latest;

            if (result.Status == UpdateStatus.UpToDate &&
                MessageBox.Show(this, "The latest version is already installed. Reinstall it anyway?",
                    Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SetBusy(true, "Downloading " + release.FileName + "…", false);
            string message = null;
            bool failed = false;
            string path = null;
            try
            {
                var report = new Progress<DownloadProgress>(OnDownloadProgress);
                path = await Task.Run(() => AmdDriverSource.Download(release, report));

                lblMessage.Text = "Verifying the digital signature…";
                progress.Style = ProgressBarStyle.Marquee;
                string downloaded = path;
                bool trusted = await Task.Run(() =>
                {
                    string signer;
                    return SignatureVerifier.IsSignedByAmd(downloaded, out signer);
                });
                if (!trusted)
                {
                    TryDelete(path);
                    throw new InvalidDataException(
                        "The downloaded file is not signed by AMD, so it was deleted and not run.");
                }

                lblMessage.Text = "Waiting for the AMD installer to finish…";
                Process installer = Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(path),
                });
                if (installer != null)
                    await Task.Run(() => installer.WaitForExit());

                message = "The AMD installer has closed. If it asked for a restart, restart Windows to finish the update.";
            }
            catch (Win32Exception ex)
            {
                failed = true;
                message = ex.NativeErrorCode == 1223
                    ? "Installation cancelled: administrator permission was not granted."
                    : "Could not start the installer: " + ex.Message;
            }
            catch (Exception ex)
            {
                failed = true;
                message = "Installation failed: " + ex.Message;
            }
            finally
            {
                SetBusy(false, null, false);
            }

            RunCheckAfterInstall(message, failed);
        }

        async void RunCheckAfterInstall(string message, bool failed)
        {
            SetBusy(true, "Checking the installed version…", true);
            try
            {
                result = await Task.Run(() => UpdateChecker.Check());
                ShowResult(result);
            }
            finally
            {
                SetBusy(false, null, false);
            }
            if (message != null)
            {
                lblMessage.Text = message + (failed ? "" : "\n" + (result.Message ?? ""));
                lblMessage.ForeColor = failed ? Red : ForeColor;
            }
        }

        void OnDownloadProgress(DownloadProgress p)
        {
            string done = (p.Received / 1048576.0).ToString("0.0");
            if (p.Total > 0)
            {
                progress.Style = ProgressBarStyle.Continuous;
                progress.Value = (int)Math.Min(100, p.Received * 100 / p.Total);
                lblMessage.Text = "Downloading… " + done + " / " + (p.Total / 1048576.0).ToString("0.0") + " MB";
            }
            else
            {
                lblMessage.Text = "Downloading… " + done + " MB";
            }
        }

        // ------------------------------------------------------------ autostart

        void OnAutostartChanged(object sender, EventArgs e)
        {
            try
            {
                Autostart.SetEnabled(chkAutostart.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not change the startup setting: " + ex.Message,
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                chkAutostart.CheckedChanged -= OnAutostartChanged;
                chkAutostart.Checked = !chkAutostart.Checked;
                chkAutostart.CheckedChanged += OnAutostartChanged;
            }
        }

        // -------------------------------------------------------------- helpers

        void SetBusy(bool isBusy, string message, bool marquee)
        {
            busy = isBusy;
            btnCheck.Enabled = !isBusy;
            btnInstall.Enabled = !isBusy && result != null && result.Latest != null
                                 && result.Status != UpdateStatus.Unsupported;
            progress.Visible = isBusy;
            if (isBusy)
            {
                progress.Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                progress.Value = 0;
                lblMessage.Text = message;
                lblMessage.ForeColor = ForeColor;
            }
            UseWaitCursor = false;
        }

        static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (Exception) { }
        }

        static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        int S(int px) { return (int)Math.Round(px * scale); }

        Padding ValueMargin() { return new Padding(S(12), S(3), 0, S(3)); }

        Label MutedLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Muted, MaximumSize = new Size(S(520), 0) };
        }

        Label Section(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Muted,
                Font = new Font("Segoe UI Semibold", 8.25f),
                Margin = new Padding(0, S(16), 0, S(4)),
            };
        }

        Button MakeButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(S(96), S(28)),
                Margin = new Padding(S(8), 0, 0, 0),
                UseVisualStyleBackColor = true,
            };
        }

        Label AddRow(TableLayoutPanel t, string caption)
        {
            var value = new Label { AutoSize = true, MaximumSize = new Size(S(400), 0), Margin = ValueMargin(), Text = "…" };
            AddRow(t, caption, value);
            return value;
        }

        void AddRow(TableLayoutPanel t, string caption, Control value)
        {
            int row = t.RowCount++;
            t.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, S(3), 0, S(3)) }, 0, row);
            t.Controls.Add(value, 1, row);
        }

        static void AddWide(TableLayoutPanel t, Control c)
        {
            int row = t.RowCount++;
            t.Controls.Add(c, 0, row);
            t.SetColumnSpan(c, 2);
        }
    }
}
