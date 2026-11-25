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

        const string IntegratedChipset = "Integrated";

        readonly float scale;
        readonly Label lblCpu, lblBoard, lblPlatformMode, lblInstalled, lblLatest, lblStatus, lblMessage, lblNote;
        readonly ComboBox cmbSocket, cmbChipset;
        readonly LinkLabel lnkPage, lnkResetPlatform;
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

            AddWide(root, new Label
            {
                Text = Program.AppName,
                Font = new Font("Segoe UI Semibold", 15f),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, S(2)),
            });
            AddWide(root, MutedLabel("Finds the right AMD chipset driver for this PC and keeps it up to date."));

            // --- System
            AddWide(root, Section("SYSTEM"));
            lblCpu = AddRow(root, "Processor");
            lblBoard = AddRow(root, "Motherboard");

            cmbSocket = MakeCombo(S(84));
            cmbChipset = MakeCombo(S(84));
            lblPlatformMode = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(S(6), S(4), 0, 0) };
            lnkResetPlatform = new LinkLabel { Text = "Reset to auto", AutoSize = true, Margin = new Padding(S(6), S(4), 0, 0), Visible = false };
            AddRow(root, "Platform", Flow(cmbSocket, cmbChipset, lblPlatformMode, lnkResetPlatform));

            // --- Driver
            AddWide(root, Section("CHIPSET DRIVER"));
            lblInstalled = AddRow(root, "Installed version");
            lblLatest = new Label { AutoSize = true, Margin = new Padding(0), Text = "…" };
            lnkPage = new LinkLabel { AutoSize = true, Margin = new Padding(S(14), 0, 0, 0), Text = "Open on amd.com", Visible = false };
            AddRow(root, "Latest version", Flow(lblLatest, lnkPage));
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

            // --- Bottom bar
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

            var footer = MutedLabel("Version " + VersionUtil.Short(AppUpdater.CurrentVersion)
                                    + "  ·  Unofficial tool, not affiliated with AMD. Drivers are downloaded from amd.com.");
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
            lnkPage.LinkClicked += delegate { OpenUrl(lnkPage.Tag as string); };
            cmbSocket.SelectionChangeCommitted += OnSocketChosen;
            cmbChipset.SelectionChangeCommitted += OnChipsetChosen;
            lnkResetPlatform.LinkClicked += delegate
            {
                Settings.ClearManualPlatform();
                RunCheck();
            };
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await RunCheckAsync();
            await CheckForAppUpdateAsync();
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
            await RunCheckAsync();
        }

        async Task RunCheckAsync()
        {
            if (busy) return;
            SetBusy(true, "Checking your hardware and amd.com…", true);
            lblStatus.Text = "Checking…";
            lblStatus.ForeColor = Muted;
            try
            {
                result = await Task.Run(() => UpdateChecker.Check());
            }
            finally
            {
                SetBusy(false, null, false);
            }
            ShowResult(result);
        }

        void ShowResult(CheckResult r)
        {
            HardwareInfo hw = r.Hardware;
            lblCpu.Text = hw != null && hw.CpuName != null ? hw.CpuName : "Unknown";
            lblBoard.Text = hw != null ? hw.BoardText : "Unknown";
            ShowPlatform(hw);

            string page = r.Latest != null ? r.Latest.PageUrl : (hw != null ? hw.DriverPageUrl : null);
            lnkPage.Tag = page;
            lnkPage.Visible = page != null;

            lblInstalled.Text = r.Installed != null ? r.Installed.VersionText : "Not installed";
            lblLatest.Text = r.Latest != null ? r.Latest.VersionText : "—";

            switch (r.Status)
            {
                case UpdateStatus.UpToDate: SetStatus("Updated", Green, "Reinstall"); break;
                case UpdateStatus.UpdateAvailable: SetStatus("Update available", Orange, "Update"); break;
                case UpdateStatus.NotInstalled: SetStatus("Not installed", Orange, "Install"); break;
                case UpdateStatus.Unsupported: SetStatus("Not supported", Muted, "Install"); break;
                default: SetStatus("Check failed", Red, "Install"); break;
            }
            lblMessage.Text = r.Message ?? "";
            lblMessage.ForeColor = r.Status == UpdateStatus.Error ? Red : ForeColor;

            bool hasNote = hw != null && !string.IsNullOrEmpty(hw.Note);
            lblNote.Text = hasNote ? hw.Note : "";
            lblNote.Visible = hasNote;
            UpdateEnabledState();
        }

        void SetStatus(string text, Color color, string buttonText)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
            btnInstall.Text = buttonText;
        }

        // ------------------------------------------------------------- platform

        void ShowPlatform(HardwareInfo hw)
        {
            cmbSocket.Items.Clear();
            cmbChipset.Items.Clear();
            lnkResetPlatform.Visible = false;

            if (hw == null || !hw.IsSupported)
            {
                lblPlatformMode.Text = hw == null ? "" : "Not supported";
                lblPlatformMode.ForeColor = Muted;
                return;
            }

            cmbSocket.Items.AddRange(AmdPlatforms.Sockets);
            cmbSocket.SelectedItem = hw.Socket;
            FillChipsets(hw.Socket, hw.Chipset);

            if (!hw.ChipsetKnown)
            {
                // Shorter text when "Reset to auto" is shown too, so both fit on the row.
                lblPlatformMode.Text = hw.IsManual ? "Select chipset" : "Not detected, please select";
                lblPlatformMode.ForeColor = Orange;
            }
            else
            {
                lblPlatformMode.Text = hw.IsManual ? "(manual)" : "(auto)";
                lblPlatformMode.ForeColor = Muted;
            }
            lnkResetPlatform.Visible = hw.IsManual;
        }

        void FillChipsets(string socket, string selected)
        {
            cmbChipset.Items.Clear();
            string[] chipsets = AmdPlatforms.ChipsetsFor(socket);
            if (chipsets.Length == 0)
            {
                cmbChipset.Items.Add(IntegratedChipset);
                cmbChipset.SelectedIndex = 0;
                return;
            }
            cmbChipset.Items.AddRange(chipsets);
            if (selected != null) cmbChipset.SelectedItem = selected;
        }

        void OnSocketChosen(object sender, EventArgs e)
        {
            SaveManualPlatform(cmbSocket.SelectedItem as string, null);
        }

        void OnChipsetChosen(object sender, EventArgs e)
        {
            string chipset = cmbChipset.SelectedItem as string;
            SaveManualPlatform(cmbSocket.SelectedItem as string, chipset == IntegratedChipset ? null : chipset);
        }

        /// <summary>Saves the user's choice (or goes back to auto if it equals the detection) and checks again.</summary>
        void SaveManualPlatform(string socket, string chipset)
        {
            HardwareInfo hw = result != null ? result.Hardware : null;
            if (socket == null || hw == null) return;
            try
            {
                if (socket == hw.DetectedSocket && chipset == hw.DetectedChipset)
                    Settings.ClearManualPlatform();
                else
                    Settings.SetManualPlatform(socket, chipset);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the platform: " + ex.Message, Program.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RunCheck();
        }

        // ------------------------------------------------------- driver install

        async void RunInstall()
        {
            if (busy || result == null || result.Latest == null) return;
            DriverRelease release = result.Latest;

            if (result.Status == UpdateStatus.UpToDate &&
                MessageBox.Show(this, "The latest version is already installed. Reinstall it anyway?",
                    Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SetBusy(true, "Downloading " + release.FileName + "…", false);
            string message;
            bool failed = false;
            try
            {
                var report = new Progress<DownloadProgress>(OnDownloadProgress);
                string path = await Task.Run(() => AmdDriverSource.Download(release, report));

                lblMessage.Text = "Verifying the digital signature…";
                progress.Style = ProgressBarStyle.Marquee;
                bool trusted = await Task.Run(() =>
                {
                    string signer;
                    return SignatureVerifier.IsSignedByAmd(path, out signer);
                });
                if (!trusted)
                {
                    TryDelete(path);
                    throw new InvalidDataException("The downloaded file is not signed by AMD, so it was deleted and not run.");
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

            await RunCheckAsync();
            lblMessage.Text = message + (failed ? "" : "\n" + (result.Message ?? ""));
            lblMessage.ForeColor = failed ? Red : ForeColor;
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

        // ----------------------------------------------------------- app update

        async Task CheckForAppUpdateAsync()
        {
            string exe = Application.ExecutablePath;
            bool installed = AppUpdater.IsInstalledCopy(exe);
            AppRelease release;
            try
            {
                release = await Task.Run(() => AppUpdater.GetLatest(installed));
            }
            catch (Exception)
            {
                return; // offline or GitHub unavailable: try again next time
            }
            if (release == null || release.Version <= AppUpdater.CurrentVersion) return;
            if (release.VersionText == Settings.SkippedAppVersion) return;

            AppUpdateChoice choice;
            using (var dialog = new AppUpdateDialog(release, AppUpdater.CurrentVersion))
            {
                dialog.ShowDialog(this);
                choice = dialog.Choice;
            }

            if (choice == AppUpdateChoice.Skip)
            {
                try { Settings.SkippedAppVersion = release.VersionText; } catch (Exception) { }
            }
            else if (choice == AppUpdateChoice.UpdateNow)
            {
                await InstallAppUpdateAsync(release, exe, installed);
            }
        }

        async Task InstallAppUpdateAsync(AppRelease release, string exe, bool installed)
        {
            if (busy) return;
            SetBusy(true, "Downloading " + Program.AppName + " " + release.VersionText + "…", false);
            try
            {
                var report = new Progress<DownloadProgress>(OnDownloadProgress);
                ProcessStartInfo next = await Task.Run(() => AppUpdater.Prepare(release, exe, installed, report));
                if (installed) Program.ReleaseRunningMutex();
                Process.Start(next);
                busy = false;
                Close(); // the installer / new exe takes over
            }
            catch (Exception ex)
            {
                SetBusy(false, null, false);
                if (result != null) ShowResult(result);
                if (MessageBox.Show(this, "The update could not be installed: " + ex.Message
                                          + "\n\nOpen the download page instead?", Program.AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    OpenUrl(release.PageUrl);
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
            progress.Visible = isBusy;
            if (isBusy)
            {
                progress.Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                progress.Value = 0;
                lblMessage.Text = message;
                lblMessage.ForeColor = ForeColor;
            }
            UpdateEnabledState();
        }

        void UpdateEnabledState()
        {
            bool supported = result != null && result.Hardware != null && result.Hardware.IsSupported;
            btnCheck.Enabled = !busy;
            btnInstall.Enabled = !busy && supported && result.Latest != null;
            cmbSocket.Enabled = !busy && supported;
            cmbChipset.Enabled = !busy && supported
                                 && AmdPlatforms.ChipsetsFor(cmbSocket.SelectedItem as string).Length > 0;
            lnkResetPlatform.Enabled = !busy;
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

        ComboBox MakeCombo(int width)
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = width,
                Margin = new Padding(0, 0, S(6), 0),
                Enabled = false,
            };
        }

        FlowLayoutPanel Flow(params Control[] controls)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Margin = ValueMargin(),
                Padding = new Padding(0),
            };
            panel.Controls.AddRange(controls);
            return panel;
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
            var label = new Label { Text = caption, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, S(3), 0, S(3)) };
            if (value is FlowLayoutPanel && ((FlowLayoutPanel)value).Controls[0] is ComboBox)
                label.Margin = new Padding(0, S(7), 0, S(3)); // align with the combo text
            t.Controls.Add(label, 0, row);
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
