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
            private static readonly Color BgColor = Color.FromArgb(18, 22, 28);
            private static readonly Color CardColor = Color.FromArgb(28, 33, 42);
            private static readonly Color FgColor = Color.WhiteSmoke;
            private static readonly Color MutedColor = Color.FromArgb(160, 170, 184);
            private static readonly Color ButtonBg = Color.FromArgb(48, 53, 64);
            private static readonly Color ButtonBorder = Color.FromArgb(80, 90, 105);
            private static readonly Color AccentColor = Color.FromArgb(110, 200, 255);

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
                Width = 980;
                Height = 660;
                MinimumSize = new Size(720, 520);
                BackColor = BgColor;
                ForeColor = FgColor;
                Font = new Font("Segoe UI", 9.25f);
                AutoScaleMode = AutoScaleMode.Dpi;
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
                page.BackColor = BgColor;
                page.ForeColor = FgColor;
                page.Padding = new Padding(8);

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.ColumnCount = 1;
                layout.RowCount = 3;
                layout.BackColor = BgColor;
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

                Label header = new Label();
                header.AutoSize = true;
                header.Text = "Mount and unmount your cloud remotes. Status updates live every few seconds.";
                header.ForeColor = MutedColor;
                header.Margin = new Padding(4, 4, 4, 6);

                FlowLayoutPanel topBar = new FlowLayoutPanel();
                topBar.AutoSize = true;
                topBar.Dock = DockStyle.Fill;
                topBar.FlowDirection = FlowDirection.LeftToRight;
                topBar.Margin = new Padding(0, 0, 0, 8);

                topBar.Controls.Add(MakeAction("Mount all", delegate { owner.MountAllProfiles(); }));
                topBar.Controls.Add(MakeAction("Unmount all", delegate { owner.UnmountAllProfiles(); }));
                topBar.Controls.Add(MakeAction("Add cloud remote...", delegate { owner.ShowManageRemotesWindow(); }));
                topBar.Controls.Add(MakeAction("Manage remotes...", delegate { owner.ShowManageRemotesWindow(); }));
                topBar.Controls.Add(MakeAction("Refresh now", delegate { owner.QueueRefresh(true, true); }));

                profilesPanel = new FlowLayoutPanel();
                profilesPanel.Dock = DockStyle.Fill;
                profilesPanel.AutoScroll = true;
                profilesPanel.FlowDirection = FlowDirection.LeftToRight;
                profilesPanel.WrapContents = true;
                profilesPanel.Padding = new Padding(0);
                profilesPanel.BackColor = BgColor;

                layout.Controls.Add(header, 0, 0);
                layout.Controls.Add(topBar, 0, 1);
                layout.Controls.Add(profilesPanel, 0, 2);

                page.Controls.Add(layout);
                return page;
            }

            private TabPage BuildDiagnosticsTab()
            {
                TabPage page = new TabPage("Diagnostics");
                page.BackColor = BgColor;
                page.ForeColor = FgColor;
                page.Padding = new Padding(8);

                FlowLayoutPanel actions = new FlowLayoutPanel();
                actions.Dock = DockStyle.Bottom;
                actions.AutoSize = true;
                actions.FlowDirection = FlowDirection.LeftToRight;
                actions.WrapContents = true;
                actions.Padding = new Padding(0, 6, 0, 0);

                actions.Controls.Add(MakeAction("Copy", delegate { try { Clipboard.SetText(diagBox.Text); } catch (Exception ex) { owner.LogUiIssue("copy diag", ex); } }));
                actions.Controls.Add(MakeAction("Refresh", delegate { diagBox.Text = owner.BuildDiagnosticsText(); }));
                actions.Controls.Add(MakeAction("Open log folder", delegate { owner.OpenLogFolder(); }));
                actions.Controls.Add(MakeAction("Open settings file", delegate { owner.OpenSettingsFile(); }));
                actions.Controls.Add(MakeAction("rclone config", delegate { owner.OpenRcloneConfigTerminal(); }));

                diagBox = new TextBox();
                diagBox.Multiline = true;
                diagBox.ReadOnly = true;
                diagBox.ScrollBars = ScrollBars.Vertical;
                diagBox.Font = new Font("Consolas", 9.25f);
                diagBox.Dock = DockStyle.Fill;
                diagBox.BackColor = Color.FromArgb(14, 18, 24);
                diagBox.ForeColor = FgColor;
                diagBox.Text = owner.BuildDiagnosticsText();

                page.Controls.Add(diagBox);
                page.Controls.Add(actions);
                return page;
            }

            private TabPage BuildLogsTab()
            {
                TabPage page = new TabPage("Logs");
                page.BackColor = BgColor;
                page.ForeColor = FgColor;
                page.Padding = new Padding(8);

                FlowLayoutPanel topBar = new FlowLayoutPanel();
                topBar.Dock = DockStyle.Top;
                topBar.AutoSize = true;
                topBar.FlowDirection = FlowDirection.LeftToRight;

                Label sel = new Label();
                sel.AutoSize = true;
                sel.Text = "Log:";
                sel.ForeColor = FgColor;
                sel.Margin = new Padding(4, 8, 6, 0);

                logSelector = new ComboBox();
                logSelector.DropDownStyle = ComboBoxStyle.DropDownList;
                logSelector.Width = 360;
                logSelector.BackColor = Color.FromArgb(14, 18, 24);
                logSelector.ForeColor = FgColor;
                logSelector.Margin = new Padding(0, 4, 6, 0);
                PopulateLogSelector();
                logSelector.SelectedIndexChanged += delegate { RefreshLogBox(); };

                topBar.Controls.Add(sel);
                topBar.Controls.Add(logSelector);
                topBar.Controls.Add(MakeAction("Refresh", delegate { PopulateLogSelector(); RefreshLogBox(); }));

                logBox = new TextBox();
                logBox.Multiline = true;
                logBox.ReadOnly = true;
                logBox.ScrollBars = ScrollBars.Vertical;
                logBox.Font = new Font("Consolas", 9.25f);
                logBox.Dock = DockStyle.Fill;
                logBox.BackColor = Color.FromArgb(14, 18, 24);
                logBox.ForeColor = FgColor;

                page.Controls.Add(logBox);
                page.Controls.Add(topBar);
                RefreshLogBox();
                return page;
            }

            private TabPage BuildSettingsTab()
            {
                TabPage page = new TabPage("Settings");
                page.BackColor = BgColor;
                page.ForeColor = FgColor;
                page.Padding = new Padding(16);
                page.AutoScroll = true;

                TableLayoutPanel grid = new TableLayoutPanel();
                grid.Dock = DockStyle.Top;
                grid.ColumnCount = 2;
                grid.AutoSize = true;
                grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                string[] choices = new string[] { "off", "512K", "1M", "5M", "10M", "25M", "50M", "100M", "250M" };
                bandwidthCombo = new ComboBox();
                bandwidthCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                bandwidthCombo.Width = 220;
                bandwidthCombo.BackColor = Color.FromArgb(14, 18, 24);
                bandwidthCombo.ForeColor = FgColor;
                bandwidthCombo.Margin = new Padding(0, 4, 12, 4);
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

                startupCheck = new CheckBox();
                startupCheck.AutoSize = true;
                startupCheck.Text = "Enabled";
                startupCheck.ForeColor = FgColor;
                startupCheck.Margin = new Padding(0, 8, 0, 8);
                startupCheck.CheckedChanged += delegate
                {
                    if (startupCheck.Checked != owner.StartupEnabled()) owner.ToggleStartup();
                };

                verboseCheck = new CheckBox();
                verboseCheck.AutoSize = true;
                verboseCheck.Text = "Write [debug] entries to pixelpipe-ui.log";
                verboseCheck.ForeColor = FgColor;
                verboseCheck.Margin = new Padding(0, 8, 0, 8);
                verboseCheck.CheckedChanged += delegate
                {
                    owner.verboseLogging = verboseCheck.Checked;
                    owner.SaveSetting("VerboseLogging", verboseCheck.Checked ? "1" : "0");
                };

                int row = 0;
                AddSettingRow(grid, row++, "Bandwidth limit (live):", bandwidthCombo);
                AddSettingRow(grid, row++, "Custom bandwidth:", MakeAction("Set custom...", delegate { owner.SetCustomBandwidth(); }));
                AddSettingRow(grid, row++, "Auto-mount at Windows startup:", startupCheck);
                AddSettingRow(grid, row++, "Verbose logging:", verboseCheck);
                AddSettingRow(grid, row++, "Setup wizard:", MakeAction("Run setup wizard", delegate { owner.RunFirstLaunchSetup(true); }));
                AddSettingRow(grid, row++, "Updates:", MakeAction("Check for updates", delegate { owner.CheckForUpdates(); }));

                page.Controls.Add(grid);
                return page;
            }

            private static void AddSettingRow(TableLayoutPanel grid, int row, string labelText, Control control)
            {
                grid.RowCount = row + 1;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Label l = new Label();
                l.AutoSize = true;
                l.Text = labelText;
                l.ForeColor = FgColor;
                l.Margin = new Padding(0, 8, 16, 0);
                grid.Controls.Add(l, 0, row);
                grid.Controls.Add(control, 1, row);
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

            // AutoSize, padded button. The previous version used fixed pixel widths
            // which clipped longer captions like "Add cloud remote..." and "Refresh now".
            private static Button MakeAction(string text, EventHandler onClick)
            {
                Button b = new Button();
                b.Text = text;
                b.AutoSize = true;
                b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                b.MinimumSize = new Size(0, 30);
                b.Padding = new Padding(10, 4, 10, 4);
                b.Margin = new Padding(2, 2, 6, 2);
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = ButtonBg;
                b.ForeColor = FgColor;
                b.FlatAppearance.BorderColor = ButtonBorder;
                b.UseVisualStyleBackColor = false;
                if (onClick != null) b.Click += onClick;
                return b;
            }
        }

        private sealed class ProfileCard
        {
            private static readonly Color BgColor = Color.FromArgb(28, 33, 42);
            private static readonly Color FgColor = Color.WhiteSmoke;
            private static readonly Color MutedColor = Color.FromArgb(160, 170, 184);
            private static readonly Color MountedPill = Color.FromArgb(50, 130, 60);
            private static readonly Color UnmountedPill = Color.FromArgb(70, 76, 88);
            private static readonly Color ButtonBg = Color.FromArgb(48, 53, 64);
            private static readonly Color ButtonBorder = Color.FromArgb(80, 90, 105);
            private static readonly Color ErrorColor = Color.FromArgb(255, 110, 110);

            private readonly TrayContext owner;
            public readonly RemoteProfile Profile;
            public readonly Panel Root;
            private readonly Label titleLabel;
            private readonly Label statusPill;
            private readonly Label remoteLabel;
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
                Root.Width = 440;
                Root.AutoSize = true;
                Root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Root.Margin = new Padding(8);
                Root.Padding = new Padding(12);
                Root.BackColor = BgColor;
                Root.BorderStyle = BorderStyle.FixedSingle;
                Root.MinimumSize = new Size(440, 0);

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Top;
                layout.AutoSize = true;
                layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                layout.ColumnCount = 1;
                layout.BackColor = BgColor;
                layout.Margin = new Padding(0);
                layout.Padding = new Padding(0);
                layout.Width = 416; // Root.Width - 2*padding

                // Header row: title (left) + status pill (right)
                TableLayoutPanel header = new TableLayoutPanel();
                header.ColumnCount = 2;
                header.RowCount = 1;
                header.AutoSize = true;
                header.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                header.Margin = new Padding(0, 0, 0, 6);
                header.Padding = new Padding(0);
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                header.BackColor = BgColor;
                header.Dock = DockStyle.Top;

                titleLabel = new Label();
                titleLabel.AutoSize = true;
                titleLabel.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
                titleLabel.ForeColor = FgColor;
                titleLabel.Margin = new Padding(0, 4, 0, 0);

                statusPill = new Label();
                statusPill.AutoSize = true;
                statusPill.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                statusPill.ForeColor = FgColor;
                statusPill.Padding = new Padding(8, 3, 8, 3);
                statusPill.Margin = new Padding(8, 4, 0, 0);

                header.Controls.Add(titleLabel, 0, 0);
                header.Controls.Add(statusPill, 1, 0);

                remoteLabel = MakeLine();
                driveLabel = MakeLine();
                statusLabel = MakeLine();
                storageLabel = MakeLine();

                storageBar = new ProgressBar();
                storageBar.Style = ProgressBarStyle.Continuous;
                storageBar.Height = 8;
                storageBar.Width = 400;
                storageBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                storageBar.Margin = new Padding(0, 2, 0, 6);

                trafficLabel = MakeLine();
                speedLabel = MakeLine();

                errorLabel = MakeLine();
                errorLabel.ForeColor = ErrorColor;
                errorLabel.MaximumSize = new Size(400, 0);

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.AutoSize = true;
                buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                buttons.FlowDirection = FlowDirection.LeftToRight;
                buttons.Margin = new Padding(0, 8, 0, 0);
                buttons.BackColor = BgColor;

                mountLow = MakeCardAction("Mount", delegate { owner.MountProfile(Profile, false); });
                mountFull = MakeCardAction("Mount (full cache)", delegate { owner.MountProfile(Profile, true); });
                unmount = MakeCardAction("Unmount", delegate { owner.UnmountProfile(Profile, false); });
                openDrive = MakeCardAction("Open", delegate { owner.OpenDrive(Profile); });

                buttons.Controls.Add(mountLow);
                buttons.Controls.Add(mountFull);
                buttons.Controls.Add(unmount);
                buttons.Controls.Add(openDrive);

                layout.Controls.Add(header);
                layout.Controls.Add(remoteLabel);
                layout.Controls.Add(driveLabel);
                layout.Controls.Add(statusLabel);
                layout.Controls.Add(storageLabel);
                layout.Controls.Add(storageBar);
                layout.Controls.Add(trafficLabel);
                layout.Controls.Add(speedLabel);
                layout.Controls.Add(errorLabel);
                layout.Controls.Add(buttons);

                Root.Controls.Add(layout);
                ApplyLiveState();
            }

            public void ApplyLiveState()
            {
                bool mounted = owner.IsMounted(Profile);
                titleLabel.Text = Profile.Label + "  (" + TrayContext.DisplayProvider(Profile.Provider) + ")";
                statusPill.Text = mounted ? " MOUNTED " : " unmounted ";
                statusPill.BackColor = mounted ? MountedPill : UnmountedPill;

                remoteLabel.Text = "Remote: " + Profile.Remote;
                driveLabel.Text = "Drive: " + owner.GetDriveRoot(Profile);
                statusLabel.Text = "Status: " + Profile.StatusText;
                storageLabel.Text = "Storage: " + Profile.StorageText;
                storageBar.Value = TrayContext.ParseStoragePercent(Profile.StorageText);
                trafficLabel.Text = "Session traffic: " + Profile.SessionText;
                speedLabel.Text = "Speed: " + Profile.SpeedText;

                bool hasError = !String.IsNullOrWhiteSpace(Profile.LastError);
                errorLabel.Visible = hasError;
                if (hasError) errorLabel.Text = "Last error: " + TrayContext.TrimForMenu(Profile.LastError, 200);

                mountLow.Enabled = !mounted;
                mountFull.Enabled = !mounted;
                unmount.Enabled = mounted;
                openDrive.Enabled = mounted;
                openDrive.Text = mounted ? "Open " + owner.GetDriveRoot(Profile) : "Open";
            }

            private static Label MakeLine()
            {
                Label l = new Label();
                l.AutoSize = true;
                l.ForeColor = FgColor;
                l.Font = new Font("Segoe UI", 9.25f);
                l.Margin = new Padding(0, 0, 0, 2);
                return l;
            }

            private static Button MakeCardAction(string text, EventHandler onClick)
            {
                Button b = new Button();
                b.Text = text;
                b.AutoSize = true;
                b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                b.MinimumSize = new Size(0, 30);
                b.Padding = new Padding(10, 4, 10, 4);
                b.Margin = new Padding(0, 0, 6, 0);
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = ButtonBg;
                b.ForeColor = FgColor;
                b.FlatAppearance.BorderColor = ButtonBorder;
                b.UseVisualStyleBackColor = false;
                if (onClick != null) b.Click += onClick;
                return b;
            }
        }
    }
}
