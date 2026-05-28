using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        private MainWindow mainWindow;

        private void ShowMainWindow()
        {
            if (mainWindow != null && !mainWindow.IsDisposed)
            {
                if (mainWindow.WindowState == FormWindowState.Minimized)
                {
                    mainWindow.WindowState = FormWindowState.Normal;
                }
                mainWindow.Activate();
                mainWindow.BringToFront();
                return;
            }
            mainWindow = new MainWindow(this);
            mainWindow.FormClosed += delegate { mainWindow = null; };
            mainWindow.Show();
        }

        private void UpdateMainWindowLiveState()
        {
            if (mainWindow != null && !mainWindow.IsDisposed) mainWindow.ApplyLiveState();
        }

        private void RebuildMainWindowProfiles()
        {
            if (mainWindow != null && !mainWindow.IsDisposed) mainWindow.RebuildProfileCards();
        }

        private sealed class MainWindow : Form
        {
            private readonly TrayContext owner;
            private TabControl tabs;
            private FlowLayoutPanel profilesPanel;
            private TextBox diagBox;
            private ComboBox logSelector;
            private TextBox logBox;
            private ComboBox bandwidthCombo;
            private CheckBox startupCheck;
            private CheckBox verboseCheck;
            private readonly List<ProfileCard> cards = new List<ProfileCard>();

            public MainWindow(TrayContext owner)
            {
                this.owner = owner;
                Text = "Pixelpipe";
                StartPosition = FormStartPosition.CenterScreen;
                Width = 880;
                Height = 600;
                MinimumSize = new Size(640, 480);
                BackColor = Color.FromArgb(18, 22, 28);
                ForeColor = Color.WhiteSmoke;
                Icon = owner.tray != null ? owner.tray.Icon : null;

                tabs = new TabControl();
                tabs.Dock = DockStyle.Fill;
                tabs.Appearance = TabAppearance.Normal;

                tabs.TabPages.Add(BuildProfilesTab());
                tabs.TabPages.Add(BuildDiagnosticsTab());
                tabs.TabPages.Add(BuildLogsTab());
                tabs.TabPages.Add(BuildSettingsTab());

                Controls.Add(tabs);

                RebuildProfileCards();
                ApplyLiveState();
            }

            private TabPage BuildProfilesTab()
            {
                TabPage page = new TabPage("Profiles");
                page.BackColor = Color.FromArgb(18, 22, 28);
                page.ForeColor = Color.WhiteSmoke;

                Label header = new Label();
                header.Text = "Mount and unmount your cloud remotes. Status updates live every few seconds.";
                header.ForeColor = Color.WhiteSmoke;
                header.Dock = DockStyle.Top;
                header.Height = 28;
                header.Padding = new Padding(12, 8, 12, 0);

                FlowLayoutPanel topBar = new FlowLayoutPanel();
                topBar.Dock = DockStyle.Top;
                topBar.Height = 44;
                topBar.Padding = new Padding(8, 4, 8, 4);
                topBar.FlowDirection = FlowDirection.LeftToRight;

                Button mountAll = MakeButton("Mount all", 110);
                mountAll.Click += delegate { owner.MountAllProfiles(); };
                Button unmountAll = MakeButton("Unmount all", 110);
                unmountAll.Click += delegate { owner.UnmountAllProfiles(); };
                Button addRemote = MakeButton("Add cloud remote...", 160);
                addRemote.Click += delegate { owner.ShowManageRemotesWindow(); };
                Button refreshBtn = MakeButton("Refresh now", 110);
                refreshBtn.Click += delegate { owner.QueueRefresh(true, true); };

                topBar.Controls.Add(mountAll);
                topBar.Controls.Add(unmountAll);
                topBar.Controls.Add(addRemote);
                topBar.Controls.Add(refreshBtn);

                profilesPanel = new FlowLayoutPanel();
                profilesPanel.Dock = DockStyle.Fill;
                profilesPanel.AutoScroll = true;
                profilesPanel.FlowDirection = FlowDirection.LeftToRight;
                profilesPanel.Padding = new Padding(8);

                page.Controls.Add(profilesPanel);
                page.Controls.Add(topBar);
                page.Controls.Add(header);
                return page;
            }

            private TabPage BuildDiagnosticsTab()
            {
                TabPage page = new TabPage("Diagnostics");
                page.BackColor = Color.FromArgb(18, 22, 28);
                page.ForeColor = Color.WhiteSmoke;

                diagBox = new TextBox();
                diagBox.Multiline = true;
                diagBox.ReadOnly = true;
                diagBox.ScrollBars = ScrollBars.Vertical;
                diagBox.Font = new Font("Consolas", 9f);
                diagBox.Dock = DockStyle.Fill;
                diagBox.BackColor = Color.FromArgb(14, 18, 24);
                diagBox.ForeColor = Color.WhiteSmoke;
                diagBox.Text = owner.BuildDiagnosticsText();

                FlowLayoutPanel actions = new FlowLayoutPanel();
                actions.Dock = DockStyle.Bottom;
                actions.Height = 44;
                actions.Padding = new Padding(8, 4, 8, 4);

                Button copyBtn = MakeButton("Copy", 90);
                copyBtn.Click += delegate { try { Clipboard.SetText(diagBox.Text); } catch (Exception ex) { owner.LogUiIssue("copy diag", ex); } };
                Button refreshBtn = MakeButton("Refresh", 90);
                refreshBtn.Click += delegate { diagBox.Text = owner.BuildDiagnosticsText(); };
                Button openLogs = MakeButton("Open log folder", 130);
                openLogs.Click += delegate { owner.OpenLogFolder(); };
                Button openSettings = MakeButton("Open settings file", 140);
                openSettings.Click += delegate { owner.OpenSettingsFile(); };
                Button rcloneCfg = MakeButton("rclone config", 110);
                rcloneCfg.Click += delegate { owner.OpenRcloneConfigTerminal(); };

                actions.Controls.Add(copyBtn);
                actions.Controls.Add(refreshBtn);
                actions.Controls.Add(openLogs);
                actions.Controls.Add(openSettings);
                actions.Controls.Add(rcloneCfg);

                page.Controls.Add(diagBox);
                page.Controls.Add(actions);
                return page;
            }

            private TabPage BuildLogsTab()
            {
                TabPage page = new TabPage("Logs");
                page.BackColor = Color.FromArgb(18, 22, 28);
                page.ForeColor = Color.WhiteSmoke;

                FlowLayoutPanel topBar = new FlowLayoutPanel();
                topBar.Dock = DockStyle.Top;
                topBar.Height = 44;
                topBar.Padding = new Padding(8, 8, 8, 4);

                Label sel = new Label();
                sel.Text = "Log:";
                sel.ForeColor = Color.WhiteSmoke;
                sel.AutoSize = true;
                sel.Padding = new Padding(0, 4, 6, 0);

                logSelector = new ComboBox();
                logSelector.DropDownStyle = ComboBoxStyle.DropDownList;
                logSelector.Width = 360;
                logSelector.BackColor = Color.FromArgb(14, 18, 24);
                logSelector.ForeColor = Color.WhiteSmoke;
                PopulateLogSelector();
                logSelector.SelectedIndexChanged += delegate { RefreshLogBox(); };

                Button refreshLog = MakeButton("Refresh", 90);
                refreshLog.Click += delegate { PopulateLogSelector(); RefreshLogBox(); };

                topBar.Controls.Add(sel);
                topBar.Controls.Add(logSelector);
                topBar.Controls.Add(refreshLog);

                logBox = new TextBox();
                logBox.Multiline = true;
                logBox.ReadOnly = true;
                logBox.ScrollBars = ScrollBars.Vertical;
                logBox.Font = new Font("Consolas", 9f);
                logBox.Dock = DockStyle.Fill;
                logBox.BackColor = Color.FromArgb(14, 18, 24);
                logBox.ForeColor = Color.WhiteSmoke;

                page.Controls.Add(logBox);
                page.Controls.Add(topBar);
                RefreshLogBox();
                return page;
            }

            private TabPage BuildSettingsTab()
            {
                TabPage page = new TabPage("Settings");
                page.BackColor = Color.FromArgb(18, 22, 28);
                page.ForeColor = Color.WhiteSmoke;

                TableLayoutPanel grid = new TableLayoutPanel();
                grid.Dock = DockStyle.Top;
                grid.ColumnCount = 2;
                grid.RowCount = 6;
                grid.AutoSize = true;
                grid.Padding = new Padding(16);
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                Label bwL = MakeLabel("Bandwidth limit (live):");
                bandwidthCombo = new ComboBox();
                bandwidthCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                bandwidthCombo.Width = 220;
                bandwidthCombo.BackColor = Color.FromArgb(14, 18, 24);
                bandwidthCombo.ForeColor = Color.WhiteSmoke;
                string[] choices = new string[] { "off", "512K", "1M", "5M", "10M", "25M", "50M", "100M", "250M" };
                for (int i = 0; i < choices.Length; i++)
                {
                    bandwidthCombo.Items.Add(choices[i] + (choices[i] == "off" ? "  (Unlimited)" : "/s"));
                }
                bandwidthCombo.SelectedIndexChanged += delegate
                {
                    if (bandwidthCombo.SelectedIndex < 0) return;
                    string val = choices[bandwidthCombo.SelectedIndex];
                    if (!String.Equals(val, owner.selectedBandwidth, StringComparison.OrdinalIgnoreCase))
                    {
                        owner.SetBandwidth(val);
                    }
                };

                Label customL = MakeLabel("Custom bandwidth:");
                Button customBtn = MakeButton("Set custom...", 160);
                customBtn.Click += delegate { owner.SetCustomBandwidth(); };

                Label startupL = MakeLabel("Auto-mount at Windows startup:");
                startupCheck = new CheckBox();
                startupCheck.Text = "Enabled";
                startupCheck.ForeColor = Color.WhiteSmoke;
                startupCheck.AutoSize = true;
                startupCheck.CheckedChanged += delegate
                {
                    if (startupCheck.Checked != owner.StartupEnabled()) owner.ToggleStartup();
                };

                Label verboseL = MakeLabel("Verbose logging:");
                verboseCheck = new CheckBox();
                verboseCheck.Text = "Write [debug] entries to pixelpipe-ui.log";
                verboseCheck.ForeColor = Color.WhiteSmoke;
                verboseCheck.AutoSize = true;
                verboseCheck.CheckedChanged += delegate
                {
                    owner.verboseLogging = verboseCheck.Checked;
                    owner.SaveSetting("VerboseLogging", verboseCheck.Checked ? "1" : "0");
                };

                Label setupL = MakeLabel("Setup wizard:");
                Button setupBtn = MakeButton("Run setup wizard", 160);
                setupBtn.Click += delegate { owner.RunFirstLaunchSetup(true); };

                Label updateL = MakeLabel("Updates:");
                Button updateBtn = MakeButton("Check for updates", 160);
                updateBtn.Click += delegate { owner.CheckForUpdates(); };

                grid.Controls.Add(bwL, 0, 0); grid.Controls.Add(bandwidthCombo, 1, 0);
                grid.Controls.Add(customL, 0, 1); grid.Controls.Add(customBtn, 1, 1);
                grid.Controls.Add(startupL, 0, 2); grid.Controls.Add(startupCheck, 1, 2);
                grid.Controls.Add(verboseL, 0, 3); grid.Controls.Add(verboseCheck, 1, 3);
                grid.Controls.Add(setupL, 0, 4); grid.Controls.Add(setupBtn, 1, 4);
                grid.Controls.Add(updateL, 0, 5); grid.Controls.Add(updateBtn, 1, 5);

                page.Controls.Add(grid);
                return page;
            }

            public void RebuildProfileCards()
            {
                if (profilesPanel == null) return;
                profilesPanel.SuspendLayout();
                profilesPanel.Controls.Clear();
                cards.Clear();
                RemoteProfile[] snapshot = owner.SnapshotProfiles();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    ProfileCard card = new ProfileCard(owner, snapshot[i]);
                    cards.Add(card);
                    profilesPanel.Controls.Add(card.Root);
                }
                profilesPanel.ResumeLayout();
                ApplyLiveState();
            }

            public void ApplyLiveState()
            {
                if (IsDisposed) return;
                try
                {
                    // If profile count drifted (add/remove from elsewhere), rebuild.
                    RemoteProfile[] snapshot = owner.SnapshotProfiles();
                    if (snapshot.Length != cards.Count)
                    {
                        RebuildProfileCards();
                        return;
                    }
                    for (int i = 0; i < cards.Count; i++)
                    {
                        cards[i].ApplyLiveState();
                    }
                    // Settings tab live state
                    if (bandwidthCombo != null)
                    {
                        string[] choices = new string[] { "off", "512K", "1M", "5M", "10M", "25M", "50M", "100M", "250M" };
                        int idx = Array.FindIndex(choices, s => String.Equals(s, owner.selectedBandwidth, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0 && bandwidthCombo.SelectedIndex != idx) bandwidthCombo.SelectedIndex = idx;
                    }
                    if (startupCheck != null) startupCheck.Checked = owner.StartupEnabled();
                    if (verboseCheck != null) verboseCheck.Checked = owner.verboseLogging;
                    if (diagBox != null && tabs != null && tabs.SelectedTab != null && tabs.SelectedTab.Text == "Diagnostics")
                    {
                        // Only repaint while the user is actually looking at this tab.
                        diagBox.Text = owner.BuildDiagnosticsText();
                    }
                    if (logBox != null && tabs != null && tabs.SelectedTab != null && tabs.SelectedTab.Text == "Logs")
                    {
                        RefreshLogBox();
                    }
                }
                catch (Exception ex) { owner.LogUiIssue("main window live", ex); }
            }

            private void PopulateLogSelector()
            {
                if (logSelector == null) return;
                string previous = logSelector.SelectedItem as string;
                logSelector.Items.Clear();
                logSelector.Items.Add("UI log (pixelpipe-ui.log)");
                RemoteProfile[] snapshot = owner.SnapshotProfiles();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    logSelector.Items.Add(snapshot[i].Label + " - " + snapshot[i].DriveLetter);
                }
                if (previous != null && logSelector.Items.Contains(previous)) logSelector.SelectedItem = previous;
                else if (logSelector.Items.Count > 0) logSelector.SelectedIndex = 0;
            }

            private void RefreshLogBox()
            {
                if (logBox == null || logSelector == null) return;
                int idx = logSelector.SelectedIndex;
                string text;
                if (idx <= 0)
                {
                    text = owner.TailUiLog(20000);
                }
                else
                {
                    RemoteProfile[] snapshot = owner.SnapshotProfiles();
                    int profileIdx = idx - 1;
                    if (profileIdx < snapshot.Length)
                    {
                        text = owner.TailLog(snapshot[profileIdx], 20000);
                    }
                    else
                    {
                        text = "(profile not found)";
                    }
                }
                if (logBox.Text != text)
                {
                    logBox.Text = text;
                    logBox.SelectionStart = logBox.Text.Length;
                    logBox.ScrollToCaret();
                }
            }

            private static Button MakeButton(string text, int width)
            {
                Button b = new Button();
                b.Text = text;
                b.Width = width;
                b.Height = 28;
                b.Margin = new Padding(4);
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Color.FromArgb(40, 44, 52);
                b.ForeColor = Color.WhiteSmoke;
                b.FlatAppearance.BorderColor = Color.FromArgb(70, 80, 92);
                return b;
            }

            private static Label MakeLabel(string text)
            {
                Label l = new Label();
                l.Text = text;
                l.ForeColor = Color.WhiteSmoke;
                l.AutoSize = true;
                l.Padding = new Padding(0, 6, 8, 0);
                return l;
            }
        }

        // Each profile dashboard card. Tracks the controls it owns so the parent
        // can update them in place without rebuilding.
        private sealed class ProfileCard
        {
            private readonly TrayContext owner;
            public readonly RemoteProfile Profile;
            public readonly Panel Root;
            private readonly Label titleLabel;
            private readonly Label statusPill;
            private readonly Label driveLabel;
            private readonly Label statusLabel;
            private readonly Label storageLabel;
            private readonly ProgressBar storageBar;
            private readonly Label trafficLabel;
            private readonly Label speedLabel;
            private readonly Label errorLabel;
            private readonly Button mountLow;
            private readonly Button mountFull;
            private readonly Button unmount;
            private readonly Button openDrive;

            public ProfileCard(TrayContext owner, RemoteProfile p)
            {
                this.owner = owner;
                this.Profile = p;

                Root = new Panel();
                Root.Width = 400;
                Root.Height = 220;
                Root.Margin = new Padding(8);
                Root.BackColor = Color.FromArgb(24, 28, 36);
                Root.BorderStyle = BorderStyle.FixedSingle;

                titleLabel = new Label();
                titleLabel.Text = p.Label;
                titleLabel.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
                titleLabel.ForeColor = Color.WhiteSmoke;
                titleLabel.Left = 10; titleLabel.Top = 8; titleLabel.Width = 240; titleLabel.Height = 20;

                statusPill = new Label();
                statusPill.AutoSize = false;
                statusPill.TextAlign = ContentAlignment.MiddleCenter;
                statusPill.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                statusPill.Left = 290; statusPill.Top = 10; statusPill.Width = 100; statusPill.Height = 18;

                driveLabel = MakeCardLabel(10, 32);
                statusLabel = MakeCardLabel(10, 52);
                storageLabel = MakeCardLabel(10, 72);
                storageBar = new ProgressBar();
                storageBar.Left = 10; storageBar.Top = 90; storageBar.Width = 380; storageBar.Height = 8;
                storageBar.Style = ProgressBarStyle.Continuous;
                trafficLabel = MakeCardLabel(10, 104);
                speedLabel = MakeCardLabel(10, 122);
                errorLabel = MakeCardLabel(10, 142);
                errorLabel.ForeColor = Color.FromArgb(255, 110, 110);
                errorLabel.Width = 380;

                mountLow = MakeCardButton("Mount", 10, 174, 80);
                mountLow.Click += delegate { owner.MountProfile(p, false); };
                mountFull = MakeCardButton("Full cache", 100, 174, 90);
                mountFull.Click += delegate { owner.MountProfile(p, true); };
                unmount = MakeCardButton("Unmount", 200, 174, 90);
                unmount.Click += delegate { owner.UnmountProfile(p, false); };
                openDrive = MakeCardButton("Open", 300, 174, 90);
                openDrive.Click += delegate { owner.OpenDrive(p); };

                Root.Controls.Add(titleLabel);
                Root.Controls.Add(statusPill);
                Root.Controls.Add(driveLabel);
                Root.Controls.Add(statusLabel);
                Root.Controls.Add(storageLabel);
                Root.Controls.Add(storageBar);
                Root.Controls.Add(trafficLabel);
                Root.Controls.Add(speedLabel);
                Root.Controls.Add(errorLabel);
                Root.Controls.Add(mountLow);
                Root.Controls.Add(mountFull);
                Root.Controls.Add(unmount);
                Root.Controls.Add(openDrive);

                ApplyLiveState();
            }

            public void ApplyLiveState()
            {
                bool mounted = owner.IsMounted(Profile);
                titleLabel.Text = Profile.Label + "  (" + TrayContext.DisplayProvider(Profile.Provider) + ")";
                if (mounted)
                {
                    statusPill.Text = "MOUNTED";
                    statusPill.BackColor = Color.FromArgb(50, 130, 60);
                    statusPill.ForeColor = Color.WhiteSmoke;
                }
                else
                {
                    statusPill.Text = "unmounted";
                    statusPill.BackColor = Color.FromArgb(60, 64, 72);
                    statusPill.ForeColor = Color.WhiteSmoke;
                }
                driveLabel.Text = "Drive: " + owner.GetDriveRoot(Profile) + "    Remote: " + Profile.Remote;
                statusLabel.Text = Profile.StatusText;
                storageLabel.Text = "Storage: " + Profile.StorageText;
                storageBar.Value = TrayContext.ParseStoragePercent(Profile.StorageText);
                trafficLabel.Text = "Session traffic: " + Profile.SessionText;
                speedLabel.Text = "Speed: " + Profile.SpeedText;

                bool hasError = !String.IsNullOrWhiteSpace(Profile.LastError);
                errorLabel.Visible = hasError;
                if (hasError) errorLabel.Text = "Last error: " + TrayContext.TrimForMenu(Profile.LastError, 80);

                mountLow.Enabled = !mounted;
                mountFull.Enabled = !mounted;
                unmount.Enabled = mounted;
                openDrive.Enabled = mounted;
                openDrive.Text = "Open " + owner.GetDriveRoot(Profile);
            }

            private static Label MakeCardLabel(int left, int top)
            {
                Label l = new Label();
                l.Left = left; l.Top = top; l.Width = 380; l.Height = 18;
                l.ForeColor = Color.WhiteSmoke;
                l.Font = new Font("Segoe UI", 9f);
                return l;
            }

            private static Button MakeCardButton(int left, int top, int width)
            {
                Button b = new Button();
                b.Left = left; b.Top = top; b.Width = width; b.Height = 28;
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Color.FromArgb(40, 44, 52);
                b.ForeColor = Color.WhiteSmoke;
                b.FlatAppearance.BorderColor = Color.FromArgb(70, 80, 92);
                return b;
            }

            private static Button MakeCardButton(string text, int left, int top, int width)
            {
                Button b = MakeCardButton(left, top, width);
                b.Text = text;
                return b;
            }
        }
    }
}
