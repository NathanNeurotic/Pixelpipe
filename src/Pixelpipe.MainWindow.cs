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
            ShowMainWindow(null);
        }

        private void ShowMainWindow(string tabName)
        {
            if (mainWindow != null && !mainWindow.IsDisposed)
            {
                if (mainWindow.WindowState == FormWindowState.Minimized)
                {
                    mainWindow.WindowState = FormWindowState.Normal;
                }
                if (!String.IsNullOrEmpty(tabName)) mainWindow.SelectTab(tabName);
                mainWindow.Activate();
                mainWindow.BringToFront();
                return;
            }
            mainWindow = new MainWindow(this);
            mainWindow.FormClosed += delegate { mainWindow = null; };
            mainWindow.Show();
            if (!String.IsNullOrEmpty(tabName)) mainWindow.SelectTab(tabName);
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
            // Aliases for ergonomics; the canonical palette lives in WindowTheme.
            internal static Color BgColor { get { return WindowTheme.BgColor; } }
            internal static Color CardColor { get { return WindowTheme.CardColor; } }
            internal static Color FgColor { get { return WindowTheme.FgColor; } }
            internal static Color MutedColor { get { return WindowTheme.MutedColor; } }
            internal static Color ButtonBg { get { return WindowTheme.ButtonBg; } }
            internal static Color ButtonBorder { get { return WindowTheme.ButtonBorder; } }
            internal static Color AccentColor { get { return WindowTheme.AccentColor; } }
            internal static Color WarnColor { get { return WindowTheme.WarnColor; } }

            private readonly TrayContext owner;
            private TabControl tabs;
            private Label rcloneStatusLabel;
            private Label winfspStatusLabel;
            private Label quotaLabel;
            private Label adminWarningLabel;
            private Label globalStatusLabel;
            private FlowLayoutPanel profilesPanel;
            private TextBox diagBox;
            private ComboBox logSelector;
            private TextBox logBox;
            private TextBox logFilterBox;
            private ComboBox bandwidthCombo;
            private CheckBox startupCheck;
            private CheckBox verboseCheck;
            private CheckBox updateCheck;
            private CheckBox transferNotifyCheck;
            private Label settingsRcloneStatus;
            private Label settingsWinfspStatus;
            private Label settingsRemoteStatus;
            private Label settingsApiKeyStatus;
            private Button installRcloneBtn;
            private Button installWinfspBtn;
            private Button configurePixeldrainBtn;
            private Button rcloneConfigBtn;
            private Button setApiKeyBtn;
            private Button clearApiKeyBtn;
            private readonly List<ProfileCard> cards = new List<ProfileCard>();

            public MainWindow(TrayContext owner)
            {
                this.owner = owner;
                Text = "Pixelpipe";
                StartPosition = FormStartPosition.CenterScreen;
                Width = 1040;
                Height = 720;
                MinimumSize = new Size(820, 560);
                BackColor = BgColor;
                ForeColor = FgColor;
                Font = new Font("Segoe UI", 9.25f);
                AutoScaleMode = AutoScaleMode.Dpi;
                Icon = owner.tray != null ? owner.tray.Icon : null;

                tabs = new TabControl();
                tabs.Dock = DockStyle.Fill;

                tabs.TabPages.Add(BuildProfilesTab());
                tabs.TabPages.Add(BuildDiagnosticsTab());
                tabs.TabPages.Add(BuildLogsTab());
                tabs.TabPages.Add(BuildSettingsTab());

                Controls.Add(tabs);

                // RebuildProfileCards ends by calling ApplyLiveState, so we don't
                // need to call it again here. (Used to; see v0.6.0 audit item F.)
                RebuildProfileCards();
            }

            // ----- Profiles tab -----

            private TabPage BuildProfilesTab()
            {
                TabPage page = new TabPage("Profiles");
                page.BackColor = BgColor;
                page.ForeColor = FgColor;
                page.Padding = new Padding(8);

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.ColumnCount = 1;
                layout.RowCount = 4;
                layout.BackColor = BgColor;
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // status strip
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // tagline
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // top action bar
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // cards

                layout.Controls.Add(BuildStatusStrip(), 0, 0);

                Label tagline = new Label();
                tagline.AutoSize = true;
                tagline.Text = "Mount and unmount your cloud remotes. Status updates live every few seconds.";
                tagline.ForeColor = MutedColor;
                tagline.Margin = new Padding(4, 4, 4, 8);
                layout.Controls.Add(tagline, 0, 1);

                FlowLayoutPanel topBar = new FlowLayoutPanel();
                topBar.AutoSize = true;
                topBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                topBar.FlowDirection = FlowDirection.LeftToRight;
                topBar.WrapContents = true;
                topBar.Dock = DockStyle.Fill;
                topBar.Margin = new Padding(0, 0, 0, 8);
                topBar.MaximumSize = new Size(0, 0);

                topBar.Controls.Add(MakeAction("Mount all", delegate { owner.MountAllProfiles(); }));
                topBar.Controls.Add(MakeAction("Unmount all", delegate { owner.UnmountAllProfiles(); }));
                topBar.Controls.Add(MakeAddRemoteSplitButton());
                topBar.Controls.Add(MakeAction("Import existing...", delegate { owner.ImportExistingRemotes(); }));
                topBar.Controls.Add(MakeAction("Manage remotes...", delegate { owner.ShowManageRemotesWindow(); }));
                topBar.Controls.Add(MakeAction("Export profiles...", delegate { owner.ExportProfilesToFile(); }));
                topBar.Controls.Add(MakeAction("Import profiles file...", delegate { owner.ImportProfilesFromFile(); RebuildProfileCards(); }));
                topBar.Controls.Add(MakeAction("Refresh now", delegate { owner.QueueRefresh(true, true); }));

                layout.Controls.Add(topBar, 0, 2);

                profilesPanel = new FlowLayoutPanel();
                profilesPanel.Dock = DockStyle.Fill;
                profilesPanel.AutoScroll = true;
                profilesPanel.FlowDirection = FlowDirection.LeftToRight;
                profilesPanel.WrapContents = true;
                profilesPanel.Padding = new Padding(0);
                profilesPanel.BackColor = BgColor;
                layout.Controls.Add(profilesPanel, 0, 3);

                page.Controls.Add(layout);
                return page;
            }

            private Control BuildStatusStrip()
            {
                FlowLayoutPanel strip = new FlowLayoutPanel();
                strip.AutoSize = true;
                strip.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                strip.FlowDirection = FlowDirection.LeftToRight;
                strip.WrapContents = true;
                strip.Dock = DockStyle.Fill;
                strip.Margin = new Padding(0, 0, 0, 6);
                strip.Padding = new Padding(4);
                strip.BackColor = CardColor;

                globalStatusLabel = MakeStatusChip("Status: …");
                rcloneStatusLabel = MakeStatusChip("rclone: …");
                winfspStatusLabel = MakeStatusChip("WinFsp: …");
                quotaLabel = MakeStatusChip("Transfer quota: …");
                adminWarningLabel = MakeStatusChip("Running as Administrator");
                adminWarningLabel.ForeColor = WarnColor;
                adminWarningLabel.Visible = false;

                strip.Controls.Add(globalStatusLabel);
                strip.Controls.Add(rcloneStatusLabel);
                strip.Controls.Add(winfspStatusLabel);
                strip.Controls.Add(quotaLabel);
                strip.Controls.Add(adminWarningLabel);
                return strip;
            }

            private static Label MakeStatusChip(string text)
            {
                Label l = new Label();
                l.AutoSize = true;
                l.Text = text;
                l.ForeColor = FgColor;
                l.BackColor = ButtonBg;
                l.Padding = new Padding(8, 4, 8, 4);
                l.Margin = new Padding(4);
                l.Font = new Font("Segoe UI", 9f);
                return l;
            }

            private Button MakeAddRemoteSplitButton()
            {
                Button b = MakeAction("Add cloud remote ▾", null);
                ContextMenuStrip menuStrip = new ContextMenuStrip();
                TrayMenuTheme.Apply(menuStrip);
                menuStrip.Items.Add("Pixeldrain", null, delegate { owner.AddPixeldrainProfile(); });
                menuStrip.Items.Add("Google Drive (OAuth)", null, delegate { owner.ConfigureOAuthRemoteWizard("Google Drive", "drive", "drive", "G:"); });
                menuStrip.Items.Add("OneDrive (OAuth)", null, delegate { owner.ConfigureOAuthRemoteWizard("OneDrive", "onedrive", "onedrive", "O:"); });
                menuStrip.Items.Add("Dropbox (OAuth)", null, delegate { owner.ConfigureOAuthRemoteWizard("Dropbox", "dropbox", "dropbox", "D:"); });
                menuStrip.Items.Add("Box (OAuth)", null, delegate { owner.ConfigureOAuthRemoteWizard("Box", "box", "box", "K:"); });
                menuStrip.Items.Add("MEGA", null, delegate { owner.ConfigureMegaRemoteWizard(); });
                menuStrip.Items.Add("S3 / R2 / B2 / Wasabi", null, delegate { owner.ConfigureS3RemoteWizard(); });
                menuStrip.Items.Add("WebDAV / Nextcloud / SharePoint", null, delegate { owner.ConfigureWebDAVRemoteWizard(); });
                menuStrip.Items.Add("SFTP", null, delegate { owner.ConfigureSFTPRemoteWizard(); });
                menuStrip.Items.Add("FTP / FTPS", null, delegate { owner.ConfigureFTPRemoteWizard(); });
                menuStrip.Items.Add(new ToolStripSeparator());
                menuStrip.Items.Add("Custom existing rclone remote...", null, delegate { owner.AddExistingRemoteProfile(); });
                menuStrip.Items.Add("Open rclone config terminal", null, delegate { owner.OpenRcloneConfigTerminal(); });
                b.Click += delegate
                {
                    Point p = b.PointToScreen(new Point(0, b.Height));
                    menuStrip.Show(p);
                };
                return b;
            }

            // ----- Diagnostics tab -----

            private TabPage BuildDiagnosticsTab()
            {
                TabPage page = new TabPage("Diagnostics");
                page.BackColor = BgColor;
                page.ForeColor = FgColor;
                page.Padding = new Padding(8);

                FlowLayoutPanel actions = new FlowLayoutPanel();
                actions.Dock = DockStyle.Bottom;
                actions.AutoSize = true;
                actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                actions.FlowDirection = FlowDirection.LeftToRight;
                actions.WrapContents = true;
                actions.Padding = new Padding(0, 6, 0, 0);

                actions.Controls.Add(MakeAction("Copy", delegate { try { Clipboard.SetText(diagBox.Text); } catch (Exception ex) { owner.LogUiIssue("copy diag", ex); } }));
                actions.Controls.Add(MakeAction("Refresh", delegate { diagBox.Text = owner.BuildDiagnosticsText(); }));
                actions.Controls.Add(MakeAction("Open log folder", delegate { owner.OpenLogFolder(); }));
                actions.Controls.Add(MakeAction("Open settings file", delegate { owner.OpenSettingsFile(); }));
                actions.Controls.Add(MakeAction("rclone config", delegate { owner.OpenRcloneConfigTerminal(); }));
                actions.Controls.Add(MakeAction("Clear stale primary drive", delegate { owner.CleanStaleDriveMappings(owner.GetPrimaryProfile(), true); }));

                diagBox = new TextBox();
                diagBox.Multiline = true;
                diagBox.ReadOnly = true;
                diagBox.ScrollBars = ScrollBars.Vertical;
                diagBox.Font = new Font("Consolas", 9.25f);
                diagBox.Dock = DockStyle.Fill;
                diagBox.BackColor = WindowTheme.InputBg;
                diagBox.ForeColor = FgColor;
                diagBox.Text = owner.BuildDiagnosticsText();

                page.Controls.Add(diagBox);
                page.Controls.Add(actions);
                return page;
            }

            // ----- Logs tab -----

            private TabPage BuildLogsTab()
            {
                TabPage page = new TabPage("Logs");
                page.BackColor = BgColor;
                page.ForeColor = FgColor;
                page.Padding = new Padding(8);

                FlowLayoutPanel topBar = new FlowLayoutPanel();
                topBar.Dock = DockStyle.Top;
                topBar.AutoSize = true;
                topBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                topBar.FlowDirection = FlowDirection.LeftToRight;

                Label sel = new Label();
                sel.AutoSize = true;
                sel.Text = "Log:";
                sel.ForeColor = FgColor;
                sel.Margin = new Padding(4, 8, 6, 0);

                logSelector = new ComboBox();
                logSelector.DropDownStyle = ComboBoxStyle.DropDownList;
                logSelector.Width = 360;
                logSelector.BackColor = WindowTheme.InputBg;
                logSelector.ForeColor = FgColor;
                logSelector.Margin = new Padding(0, 4, 6, 0);
                PopulateLogSelector();
                logSelector.SelectedIndexChanged += delegate { RefreshLogBox(); };

                topBar.Controls.Add(sel);
                topBar.Controls.Add(logSelector);

                Label filterLabel = new Label();
                filterLabel.AutoSize = true;
                filterLabel.Text = "Filter:";
                filterLabel.ForeColor = FgColor;
                filterLabel.Margin = new Padding(8, 8, 6, 0);

                logFilterBox = new TextBox();
                logFilterBox.Width = 200;
                logFilterBox.BackColor = WindowTheme.InputBg;
                logFilterBox.ForeColor = FgColor;
                logFilterBox.Margin = new Padding(0, 4, 6, 0);
                logFilterBox.TextChanged += delegate { RefreshLogBox(); };

                topBar.Controls.Add(filterLabel);
                topBar.Controls.Add(logFilterBox);
                topBar.Controls.Add(MakeAction("Refresh", delegate { PopulateLogSelector(); RefreshLogBox(); }));
                topBar.Controls.Add(MakeAction("Open log folder", delegate { owner.OpenLogFolder(); }));

                logBox = new TextBox();
                logBox.Multiline = true;
                logBox.ReadOnly = true;
                logBox.ScrollBars = ScrollBars.Vertical;
                logBox.Font = new Font("Consolas", 9.25f);
                logBox.Dock = DockStyle.Fill;
                logBox.BackColor = WindowTheme.InputBg;
                logBox.ForeColor = FgColor;

                page.Controls.Add(logBox);
                page.Controls.Add(topBar);
                RefreshLogBox();
                return page;
            }

            // ----- Settings tab -----

            private TabPage BuildSettingsTab()
            {
                TabPage page = new TabPage("Settings");
                page.BackColor = BgColor;
                page.ForeColor = FgColor;
                page.Padding = new Padding(16);
                page.AutoScroll = true;

                FlowLayoutPanel root = new FlowLayoutPanel();
                root.Dock = DockStyle.Top;
                root.AutoSize = true;
                root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                root.FlowDirection = FlowDirection.TopDown;
                root.WrapContents = false;

                root.Controls.Add(BuildDependenciesGroup());
                root.Controls.Add(BuildRemotesGroup());
                root.Controls.Add(BuildPreferencesGroup());
                root.Controls.Add(BuildMaintenanceGroup());

                page.Controls.Add(root);
                return page;
            }

            private GroupBox BuildDependenciesGroup()
            {
                GroupBox g = MakeGroup("Dependencies");

                TableLayoutPanel grid = MakeKeyValueGrid();

                settingsRcloneStatus = MakeValueLabel("…");
                settingsWinfspStatus = MakeValueLabel("…");

                installRcloneBtn = MakeAction("Download portable rclone", delegate { owner.DownloadRclonePortableWithUi(); ApplyLiveState(); });
                Button installRcloneWingetBtn = MakeAction("Install rclone via winget", delegate { owner.InstallRcloneWithWinget(); ApplyLiveState(); });
                installWinfspBtn = MakeAction("Install WinFsp via winget", delegate { owner.InstallWinFspWithWinget(); ApplyLiveState(); });
                rcloneConfigBtn = MakeAction("Open rclone config terminal", delegate { owner.OpenRcloneConfigTerminal(); });

                FlowLayoutPanel rcloneRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0) };
                rcloneRow.Controls.Add(settingsRcloneStatus);
                rcloneRow.Controls.Add(installRcloneBtn);
                rcloneRow.Controls.Add(installRcloneWingetBtn);

                FlowLayoutPanel winfspRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0) };
                winfspRow.Controls.Add(settingsWinfspStatus);
                winfspRow.Controls.Add(installWinfspBtn);

                AddSettingRow(grid, "rclone:", rcloneRow);
                AddSettingRow(grid, "WinFsp:", winfspRow);
                AddSettingRow(grid, "rclone remotes:", rcloneConfigBtn);

                g.Controls.Add(grid);
                return g;
            }

            private GroupBox BuildRemotesGroup()
            {
                GroupBox g = MakeGroup("Pixeldrain quota");

                TableLayoutPanel grid = MakeKeyValueGrid();

                settingsRemoteStatus = MakeValueLabel("…");
                settingsApiKeyStatus = MakeValueLabel("…");

                configurePixeldrainBtn = MakeAction("Configure Pixeldrain remote", delegate { owner.ConfigurePixeldrainRemoteFromPrompt(owner.GetPrimaryProfile()); ApplyLiveState(); });
                setApiKeyBtn = MakeAction("Set / change API key", delegate { owner.SetApiKeyFromPrompt(); ApplyLiveState(); });
                clearApiKeyBtn = MakeAction("Clear API key", delegate { owner.ClearApiKey(); ApplyLiveState(); });
                Button openApiKeyPageBtn = MakeAction("Open pixeldrain.com API keys page", delegate { owner.OpenApiKeysPage(); });

                FlowLayoutPanel apiKeyRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0) };
                apiKeyRow.Controls.Add(settingsApiKeyStatus);
                apiKeyRow.Controls.Add(setApiKeyBtn);
                apiKeyRow.Controls.Add(clearApiKeyBtn);
                apiKeyRow.Controls.Add(openApiKeyPageBtn);

                AddSettingRow(grid, "Primary remote:", settingsRemoteStatus);
                AddSettingRow(grid, "Configure:", configurePixeldrainBtn);
                AddSettingRow(grid, "API key:", apiKeyRow);

                g.Controls.Add(grid);
                return g;
            }

            private GroupBox BuildPreferencesGroup()
            {
                GroupBox g = MakeGroup("Preferences");

                TableLayoutPanel grid = MakeKeyValueGrid();

                string[] choices = new string[] { "off", "512K", "1M", "5M", "10M", "25M", "50M", "100M", "250M" };
                bandwidthCombo = new ComboBox();
                bandwidthCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                bandwidthCombo.Width = 200;
                bandwidthCombo.BackColor = WindowTheme.InputBg;
                bandwidthCombo.ForeColor = FgColor;
                bandwidthCombo.Margin = new Padding(0, 4, 12, 4);
                for (int i = 0; i < choices.Length; i++)
                {
                    bandwidthCombo.Items.Add(choices[i] == "off" ? "Unlimited" : (choices[i] + "/s"));
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

                Button customBwBtn = MakeAction("Custom...", delegate { owner.SetCustomBandwidth(); });
                FlowLayoutPanel bwRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0) };
                bwRow.Controls.Add(bandwidthCombo);
                bwRow.Controls.Add(customBwBtn);

                startupCheck = new CheckBox();
                startupCheck.AutoSize = true;
                startupCheck.Text = "Auto-mount profiles tagged AutoMount at Windows startup";
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

                updateCheck = new CheckBox();
                updateCheck.AutoSize = true;
                updateCheck.Text = "Check GitHub for new releases (once per day, on tray menu open)";
                updateCheck.ForeColor = FgColor;
                updateCheck.Margin = new Padding(0, 8, 0, 8);
                updateCheck.CheckedChanged += delegate
                {
                    owner.SaveSetting("UpdateCheckEnabled", updateCheck.Checked ? "1" : "0");
                };

                transferNotifyCheck = new CheckBox();
                transferNotifyCheck.AutoSize = true;
                transferNotifyCheck.Text = "Notify when a transfer batch finishes (≥ 10 MB)";
                transferNotifyCheck.ForeColor = FgColor;
                transferNotifyCheck.Margin = new Padding(0, 8, 0, 8);
                transferNotifyCheck.CheckedChanged += delegate
                {
                    owner.SaveSetting("TransferNotificationsEnabled", transferNotifyCheck.Checked ? "1" : "0");
                };

                AddSettingRow(grid, "Global bandwidth limit:", bwRow);
                AddSettingRow(grid, "Startup:", startupCheck);
                AddSettingRow(grid, "Verbose logging:", verboseCheck);
                AddSettingRow(grid, "Update check:", updateCheck);
                AddSettingRow(grid, "Transfer notifications:", transferNotifyCheck);

                g.Controls.Add(grid);
                return g;
            }

            private GroupBox BuildMaintenanceGroup()
            {
                GroupBox g = MakeGroup("Maintenance");

                FlowLayoutPanel row = new FlowLayoutPanel();
                row.AutoSize = true;
                row.FlowDirection = FlowDirection.LeftToRight;
                row.WrapContents = true;
                row.Margin = new Padding(8);

                row.Controls.Add(MakeAction("Run setup wizard", delegate { owner.RunFirstLaunchSetup(true); ApplyLiveState(); }));
                row.Controls.Add(MakeAction("Open log folder", delegate { owner.OpenLogFolder(); }));
                row.Controls.Add(MakeAction("Open settings file", delegate { owner.OpenSettingsFile(); }));
                row.Controls.Add(MakeAction("Copy diagnostics", delegate { owner.CopyDiagnostics(); }));
                row.Controls.Add(MakeAction("Check for updates", delegate { owner.CheckForUpdates(); }));
                // No "Exit Pixelpipe" here on purpose: the only way to quit the tray
                // app should be the tray menu's Exit item, where it sits next to "stop
                // all mounts first" prompts. A second exit button here would just be
                // an easy way to close the window by accident and lose the tray icon.

                g.Controls.Add(row);
                return g;
            }

            private static GroupBox MakeGroup(string title)
            {
                GroupBox g = new GroupBox();
                g.Text = title;
                g.AutoSize = true;
                g.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                g.Padding = new Padding(8, 10, 8, 10);
                g.Margin = new Padding(0, 0, 0, 12);
                g.ForeColor = FgColor;
                g.BackColor = BgColor;
                return g;
            }

            private static TableLayoutPanel MakeKeyValueGrid()
            {
                TableLayoutPanel grid = new TableLayoutPanel();
                grid.Dock = DockStyle.Top;
                grid.AutoSize = true;
                grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                grid.ColumnCount = 2;
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.BackColor = BgColor;
                return grid;
            }

            private static void AddSettingRow(TableLayoutPanel grid, string labelText, Control control)
            {
                int row = grid.RowCount;
                grid.RowCount = row + 1;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Label l = new Label();
                l.AutoSize = true;
                l.Text = labelText;
                l.ForeColor = FgColor;
                l.Margin = new Padding(0, 10, 16, 0);
                grid.Controls.Add(l, 0, row);
                grid.Controls.Add(control, 1, row);
            }

            private static Label MakeValueLabel(string text)
            {
                Label l = new Label();
                l.AutoSize = true;
                l.Text = text;
                l.ForeColor = FgColor;
                l.Margin = new Padding(0, 8, 12, 0);
                return l;
            }

            // ----- Card management -----

            public void SelectTab(string text)
            {
                if (tabs == null || String.IsNullOrEmpty(text)) return;
                foreach (TabPage page in tabs.TabPages)
                {
                    if (String.Equals(page.Text, text, StringComparison.OrdinalIgnoreCase))
                    {
                        tabs.SelectedTab = page;
                        return;
                    }
                }
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
                    for (int i = 0; i < cards.Count; i++) cards[i].ApplyLiveState();

                    // Status strip
                    int mounted = 0;
                    for (int i = 0; i < snapshot.Length; i++) if (owner.IsMounted(snapshot[i])) mounted++;
                    string globalText = snapshot.Length == 0
                        ? "Status: no profiles"
                        : (mounted == 0 ? "Status: no remotes mounted" : "Status: " + mounted + "/" + snapshot.Length + " mounted");
                    SafeSet(globalStatusLabel, globalText);
                    SafeSet(rcloneStatusLabel, "rclone: " + (owner.RcloneAvailable() ? "found" : "missing"));
                    SafeSet(winfspStatusLabel, "WinFsp: " + (owner.WinFspInstalled() ? "found" : "missing"));
                    SafeSet(quotaLabel, owner.transferQuotaText);
                    if (adminWarningLabel != null) adminWarningLabel.Visible = owner.IsAdministrator();

                    // Settings tab status mirrors
                    SafeSet(settingsRcloneStatus, owner.RcloneAvailable() ? "found at " + owner.rclonePath : "missing");
                    SafeSet(settingsWinfspStatus, owner.WinFspInstalled() ? "installed" : "not installed");
                    SafeSet(settingsRemoteStatus, owner.AnyRemoteConfigured() ? "configured" : "not configured");
                    SafeSet(settingsApiKeyStatus, owner.ApiKeyConfigured() ? "set (DPAPI-encrypted)" : "not set");

                    if (installRcloneBtn != null) installRcloneBtn.Enabled = !owner.RcloneAvailable();
                    if (installWinfspBtn != null) installWinfspBtn.Enabled = !owner.WinFspInstalled();
                    if (clearApiKeyBtn != null) clearApiKeyBtn.Enabled = owner.ApiKeyConfigured();

                    if (bandwidthCombo != null)
                    {
                        string[] choices = new string[] { "off", "512K", "1M", "5M", "10M", "25M", "50M", "100M", "250M" };
                        int idx = Array.FindIndex(choices, s => String.Equals(s, owner.selectedBandwidth, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0 && bandwidthCombo.SelectedIndex != idx) bandwidthCombo.SelectedIndex = idx;
                    }
                    if (startupCheck != null) startupCheck.Checked = owner.StartupEnabled();
                    if (verboseCheck != null) verboseCheck.Checked = owner.verboseLogging;
                    if (updateCheck != null)
                    {
                        bool enabled = String.Equals(owner.LoadSetting("UpdateCheckEnabled", "1"), "1", StringComparison.OrdinalIgnoreCase);
                        if (updateCheck.Checked != enabled) updateCheck.Checked = enabled;
                    }
                    if (transferNotifyCheck != null)
                    {
                        bool enabled = String.Equals(owner.LoadSetting("TransferNotificationsEnabled", "1"), "1", StringComparison.OrdinalIgnoreCase);
                        if (transferNotifyCheck.Checked != enabled) transferNotifyCheck.Checked = enabled;
                    }
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

            private static void SafeSet(Label l, string text) { if (l != null) l.Text = text; }

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
                string filter = logFilterBox == null ? "" : (logFilterBox.Text ?? "").Trim();
                if (filter.Length > 0) text = TrayContext.FilterLogText(text, filter);
                if (logBox.Text != text)
                {
                    logBox.Text = text;
                    logBox.SelectionStart = logBox.Text.Length;
                    logBox.ScrollToCaret();
                }
            }

            internal static Button MakeAction(string text, EventHandler onClick)
            {
                Button b = new Button();
                b.Text = text;
                b.AutoSize = true;
                b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                b.MinimumSize = new Size(0, 30);
                b.Padding = new Padding(12, 4, 12, 4);
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
            private static Color CardBg { get { return WindowTheme.CardColor; } }
            private static Color FgColor { get { return WindowTheme.FgColor; } }
            private static Color MutedColor { get { return WindowTheme.MutedColor; } }
            private static Color MountedPill { get { return WindowTheme.MountedPill; } }
            private static Color UnmountedPill { get { return WindowTheme.UnmountedPill; } }
            private static Color ErrorColor { get { return WindowTheme.ErrorColor; } }

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
            private readonly Label transferQuotaLabel;
            private readonly Label trafficLabel;
            private readonly Label speedLabel;
            private readonly Label objectsLabel;
            private readonly Label watchLabel;
            private readonly Label errorLabel;
            private readonly Button mountLow;
            private readonly Button mountFull;
            private readonly Button unmount;
            private readonly Button openDrive;
            private readonly Button testBtn;
            private readonly Button editBtn;
            private readonly Button setPrimaryBtn;
            private readonly Button autoMountBtn;
            private readonly Button removeBtn;

            public ProfileCard(TrayContext owner, RemoteProfile p)
            {
                this.owner = owner;
                this.Profile = p;

                // Card root: a Panel just for the border and background. Content lives
                // in a TableLayoutPanel docked Fill so the Panel's AutoSize can measure
                // a real width (Panel.AutoSize doesn't handle Dock=Left/Right children
                // well — it collapses them to zero height, which is what made the title
                // and status pill vanish in v0.5.2).
                Root = new Panel();
                Root.AutoSize = true;
                Root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Root.Margin = new Padding(8);
                Root.Padding = new Padding(14);
                Root.BackColor = CardBg;
                Root.BorderStyle = BorderStyle.FixedSingle;
                Root.MinimumSize = new Size(560, 0);

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Top;
                layout.AutoSize = true;
                layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                layout.ColumnCount = 1;
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                layout.BackColor = CardBg;
                layout.Margin = new Padding(0);
                layout.Padding = new Padding(0);
                layout.GrowStyle = TableLayoutPanelGrowStyle.AddRows;

                // Header is a TableLayoutPanel with two AutoSize columns. No percent
                // column — those force GrowAndShrink to collapse and were the cause of
                // the "unmounte" clipping bug. We just put the pill right next to the
                // title with a small gap; visually that reads as a chip on the right.
                TableLayoutPanel header = new TableLayoutPanel();
                header.AutoSize = true;
                header.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                header.ColumnCount = 2;
                header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                header.RowCount = 1;
                header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                header.Margin = new Padding(0, 0, 0, 8);
                header.Padding = new Padding(0);
                header.BackColor = CardBg;

                titleLabel = new Label();
                titleLabel.AutoSize = true;
                titleLabel.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
                titleLabel.ForeColor = FgColor;
                titleLabel.Margin = new Padding(0, 6, 16, 0);

                statusPill = new Label();
                statusPill.AutoSize = true;
                statusPill.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                statusPill.ForeColor = FgColor;
                statusPill.Padding = new Padding(10, 4, 10, 4);
                statusPill.TextAlign = ContentAlignment.MiddleCenter;
                statusPill.Margin = new Padding(0, 8, 0, 0);

                header.Controls.Add(titleLabel, 0, 0);
                header.Controls.Add(statusPill, 1, 0);

                remoteLabel = MakeLine();
                driveLabel = MakeLine();
                statusLabel = MakeLine();
                storageLabel = MakeLine();

                storageBar = new ProgressBar();
                storageBar.Style = ProgressBarStyle.Continuous;
                storageBar.Height = 6;
                storageBar.Width = 528;
                storageBar.Margin = new Padding(0, 2, 0, 8);

                transferQuotaLabel = MakeLine();
                trafficLabel = MakeLine();
                speedLabel = MakeLine();
                objectsLabel = MakeLine();
                watchLabel = MakeLine();

                errorLabel = MakeLine();
                errorLabel.ForeColor = ErrorColor;
                errorLabel.MaximumSize = new Size(528, 0);
                errorLabel.AutoSize = true;

                // Action rows: keep WrapContents off and let the card width drive the
                // layout. With Root.MinimumSize.Width = 560 the card has ~528 px of
                // content area, plenty for four AutoSize buttons in one row.
                FlowLayoutPanel primary = new FlowLayoutPanel();
                primary.AutoSize = true;
                primary.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                primary.FlowDirection = FlowDirection.LeftToRight;
                primary.WrapContents = false;
                primary.Margin = new Padding(0, 8, 0, 0);
                primary.BackColor = CardBg;

                mountLow = MainWindow.MakeAction("Mount", delegate { owner.MountProfile(Profile, false); });
                mountFull = MainWindow.MakeAction("Full cache", delegate { owner.MountProfile(Profile, true); });
                unmount = MainWindow.MakeAction("Unmount", delegate { owner.UnmountProfile(Profile, false); });
                openDrive = MainWindow.MakeAction("Open", delegate { owner.OpenDrive(Profile); });
                primary.Controls.Add(mountLow);
                primary.Controls.Add(mountFull);
                primary.Controls.Add(unmount);
                primary.Controls.Add(openDrive);

                FlowLayoutPanel secondary = new FlowLayoutPanel();
                secondary.AutoSize = true;
                secondary.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                secondary.FlowDirection = FlowDirection.LeftToRight;
                secondary.WrapContents = false;
                secondary.Margin = new Padding(0, 4, 0, 0);
                secondary.BackColor = CardBg;

                testBtn = MainWindow.MakeAction("Test", delegate { owner.TestProfile(Profile); });
                editBtn = MainWindow.MakeAction("Edit", delegate { owner.EditProfile(Profile); });
                setPrimaryBtn = MainWindow.MakeAction("Set primary", delegate { owner.MakePrimaryProfile(Profile); });
                autoMountBtn = MainWindow.MakeAction("Auto-mount: off", delegate { owner.ToggleProfileAutoMount(Profile); });
                removeBtn = MainWindow.MakeAction("Remove", delegate { owner.RemoveProfile(Profile); });
                secondary.Controls.Add(testBtn);
                secondary.Controls.Add(editBtn);
                secondary.Controls.Add(setPrimaryBtn);
                secondary.Controls.Add(autoMountBtn);
                secondary.Controls.Add(removeBtn);

                AddRow(layout, header);
                AddRow(layout, remoteLabel);
                AddRow(layout, driveLabel);
                AddRow(layout, statusLabel);
                AddRow(layout, storageLabel);
                AddRow(layout, storageBar);
                AddRow(layout, transferQuotaLabel);
                AddRow(layout, trafficLabel);
                AddRow(layout, speedLabel);
                AddRow(layout, objectsLabel);
                AddRow(layout, watchLabel);
                AddRow(layout, errorLabel);
                AddRow(layout, primary);
                AddRow(layout, secondary);

                Root.Controls.Add(layout);
                ApplyLiveState();
            }

            // TableLayoutPanel.Controls.Add(control, col, row) requires you to also bump
            // RowCount, RowStyles, etc. Doing this inline once per row gets noisy fast.
            private static void AddRow(TableLayoutPanel grid, Control c)
            {
                int row = grid.RowCount;
                grid.RowCount = row + 1;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(c, 0, row);
            }

            public void ApplyLiveState()
            {
                bool mounted = owner.IsMounted(Profile);
                titleLabel.Text = Profile.Label + "   (" + TrayContext.DisplayProvider(Profile.Provider) + ")";
                statusPill.Text = mounted ? "MOUNTED" : "unmounted";
                statusPill.BackColor = mounted ? MountedPill : UnmountedPill;

                remoteLabel.Text = "Remote: " + Profile.Remote;
                driveLabel.Text = "Drive: " + owner.GetDriveRoot(Profile);
                statusLabel.Text = "Status: " + Profile.StatusText;
                storageLabel.Text = "Storage: " + Profile.StorageText;
                int pct = TrayContext.ComputeStoragePercent(Profile.StorageUsedBytes, Profile.StorageTotalBytes);
                if (pct < 0) pct = TrayContext.ParseStoragePercent(Profile.StorageText);
                ProviderCapabilities cap = ProviderCapabilities.For(Profile.Provider);
                storageBar.Visible = cap.SupportsStorageQuota;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                storageBar.Value = pct;

                transferQuotaLabel.Visible = cap.SupportsTransferQuota;
                if (cap.SupportsTransferQuota)
                {
                    transferQuotaLabel.Text = String.IsNullOrEmpty(Profile.TransferQuotaText)
                        ? cap.DefaultTransferQuotaText()
                        : Profile.TransferQuotaText;
                }

                trafficLabel.Text = "Session traffic: " + Profile.SessionText;
                speedLabel.Text = "Speed: " + Profile.SpeedText;

                objectsLabel.Visible = cap.SupportsFileCount && Profile.ObjectCount >= 0;
                if (objectsLabel.Visible) objectsLabel.Text = "Objects: " + Profile.ObjectCount.ToString("N0");

                watchLabel.Visible = Profile.WatchFolderEnabled;
                if (Profile.WatchFolderEnabled)
                {
                    string head = "Watch (" + TrayContext.NormalizeWatchMode(Profile.WatchFolderMode) + "): "
                        + Profile.WatchQueueCount + " queued, "
                        + Profile.WatchUploadingCount + " uploading, "
                        + Profile.WatchUploadedTotal + " uploaded";
                    if (Profile.WatchFailedTotal > 0) head += ", " + Profile.WatchFailedTotal + " failed";
                    if (!String.IsNullOrEmpty(Profile.WatchLastResult)) head += " - last: " + TrayContext.TrimForMenu(Profile.WatchLastResult, 80);
                    watchLabel.Text = head;
                }

                bool hasError = !String.IsNullOrWhiteSpace(Profile.LastError);
                errorLabel.Visible = hasError;
                if (hasError) errorLabel.Text = "Last error: " + TrayContext.TrimForMenu(Profile.LastError, 200);

                mountLow.Enabled = !mounted;
                mountFull.Enabled = !mounted;
                unmount.Enabled = mounted;
                openDrive.Enabled = mounted;
                openDrive.Text = mounted ? "Open " + owner.GetDriveRoot(Profile) : "Open";

                editBtn.Enabled = !mounted;
                removeBtn.Enabled = !mounted;
                autoMountBtn.Text = "Auto-mount: " + (Profile.AutoMount ? "on" : "off");
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
        }
    }
}
