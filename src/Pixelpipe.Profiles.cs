using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        private void AddPixeldrainProfile()
        {
            RemoteProfile p = new RemoteProfile();
            p.Label = UniqueLabel("Pixeldrain");
            p.Provider = "pixeldrain";
            p.Remote = UniqueRemoteName("Pixeldrain") + ":";
            p.DriveLetter = FirstFreePreferredDrive("P:");
            p.MountMode = "network";
            lock (profilesLock) profiles.Add(p);
            AssignRuntimeFields();
            SaveProfiles();

            DialogResult r = MessageBox.Show("Create an rclone Pixeldrain remote now using your PixelDrain API key?", "Pixelpipe", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) ConfigurePixeldrainRemoteFromPrompt(p);
            RebuildMenu();
        }

        private void AddExistingRemoteProfile()
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string[] remotes = ListRcloneRemotes();
            if (remotes.Length == 0)
            {
                MessageBox.Show("No rclone remotes were found. Opening rclone config.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OpenRcloneConfigTerminal();
                return;
            }
            string selected = ChooseFromList("Add existing rclone remote", "Choose a remote:", remotes);
            if (selected == null) return;
            string label = PromptForValue("Profile label", "Display name for this remote:", RemoteNameBare(selected));
            if (label == null) return;
            string drive = PromptForValue("Drive letter", "Drive letter for this remote:", FirstFreePreferredDrive("Z:"));
            if (drive == null) return;

            RemoteProfile p = new RemoteProfile();
            p.Label = String.IsNullOrWhiteSpace(label) ? RemoteNameBare(selected) : label.Trim();
            p.Remote = NormalizeRemoteName(selected);
            p.Provider = DetectProviderForRemote(selected);
            p.DriveLetter = NormalizeDriveLetter(drive);
            p.MountMode = "network";
            lock (profilesLock) profiles.Add(p);
            AssignRuntimeFields();
            SaveProfiles();
            RebuildMenu();
        }

        private void ImportExistingRemotes()
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string[] remotes = ListRcloneRemotes();
            int added = 0;
            for (int i = 0; i < remotes.Length; i++)
            {
                string r = NormalizeRemoteName(remotes[i]);
                if (HasProfileForRemote(r)) continue;
                RemoteProfile p = new RemoteProfile();
                p.Label = RemoteNameBare(r);
                p.Remote = r;
                p.Provider = DetectProviderForRemote(r);
                p.DriveLetter = FirstFreePreferredDrive("Z:");
                p.MountMode = "network";
                lock (profilesLock) profiles.Add(p);
                added++;
            }
            AssignRuntimeFields();
            SaveProfiles();
            RebuildMenu();
            ShowBalloon("Imported " + added.ToString() + " rclone remote(s).");
        }

        private string[] ListRcloneRemotes()
        {
            try
            {
                string output = RunRcloneCapture("listremotes", 8000);
                System.Collections.Generic.List<string> result = new System.Collections.Generic.List<string>();
                string[] lines = output.Replace("\r", "").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string s = lines[i].Trim();
                    if (s.EndsWith(":")) result.Add(s);
                }
                return result.ToArray();
            }
            catch { return new string[0]; }
        }

        private string DetectProviderForRemote(string remote)
        {
            try
            {
                string bare = RemoteNameBare(remote);
                string output = RunRcloneCapture("config show " + QuoteArg(bare), 6000);
                Match m = Regex.Match(output, "type\\s*=\\s*([^\\r\\n]+)", RegexOptions.IgnoreCase);
                if (m.Success) return NormalizeProvider(m.Groups[1].Value.Trim(), remote);
            }
            catch (Exception ex) { LogUiIssue("detect provider " + remote, ex); }
            return NormalizeProvider("custom", remote);
        }

        private bool HasProfileForRemote(string remote)
        {
            string n = NormalizeRemoteName(remote);
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (String.Equals(NormalizeRemoteName(snapshot[i].Remote), n, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void ConfigurePixeldrainRemoteFromPrompt(RemoteProfile p)
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet.", "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (p == null) p = GetPrimaryProfile();
            string existing = LoadApiKey();
            string apiKey = PromptForApiKey(existing);
            if (apiKey == null) return;
            apiKey = apiKey.Trim();
            if (apiKey.Length == 0)
            {
                MessageBox.Show("No API key was entered. The Pixeldrain remote was not configured.", "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string bare = RemoteNameBare(p.Remote);
            // SEC-1 (v0.13.0): write directly to rclone.conf so the API key
            // never sits on the rclone process command line. Try the newer
            // root_folder_id schema first, then fall back to the legacy
            // directory_id naming that earlier rclone versions used.
            List<KeyValuePair<string, string>> fields = new List<KeyValuePair<string, string>>();
            fields.Add(new KeyValuePair<string, string>("api_key", apiKey));
            fields.Add(new KeyValuePair<string, string>("root_folder_id", "me"));
            string writeError = WriteRemoteToRcloneConfig(bare, "pixeldrain", fields);
            if (writeError != null || !RemoteConfigured(p))
            {
                List<KeyValuePair<string, string>> fallback = new List<KeyValuePair<string, string>>();
                fallback.Add(new KeyValuePair<string, string>("api_key", apiKey));
                fallback.Add(new KeyValuePair<string, string>("directory_id", "me"));
                writeError = WriteRemoteToRcloneConfig(bare, "pixeldrain", fallback);
            }

            if (RemoteConfigured(p))
            {
                p.Provider = "pixeldrain";
                SaveApiKey(apiKey);
                SaveProfiles();
                MessageBox.Show(p.Remote + " configured. The same API key was saved for Pixeldrain quota display.", "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                setupStatusText = GetDependencyStatusLine();
                QueueRefresh(true, false);
            }
            else
            {
                LogUiWarn("configure remote", "rclone did not report " + p.Remote + " after config write; last error: " + (writeError ?? "(none)"));
                MessageBox.Show("rclone did not report " + p.Remote + " after configuration. See pixelpipe-ui.log for details.", "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private RemoteProfile GetPrimaryProfile()
        {
            // Snapshot the primary under the lock so a concurrent Remove/Insert can't
            // shift profiles[0] out from under us between the existence check and
            // the return. AssignRuntimeFields takes the same lock internally so we
            // call it outside this block.
            RemoteProfile primary;
            bool needsAssign;
            lock (profilesLock)
            {
                if (profiles.Count == 0) profiles.Add(new RemoteProfile());
                primary = profiles[0];
                needsAssign = String.IsNullOrWhiteSpace(primary.Id);
            }
            if (needsAssign) AssignRuntimeFields();
            return primary;
        }

        private void MakePrimaryProfile(RemoteProfile p)
        {
            if (p == null) return;
            lock (profilesLock)
            {
                if (profiles.Count < 2) return;
                profiles.Remove(p);
                profiles.Insert(0, p);
            }
            AssignRuntimeFields();
            SaveProfiles();
            RebuildMenu();
            ShowBalloon(p.Label + " set as primary.");
        }

        private void EditProfile(RemoteProfile p)
        {
            if (p == null) return;
            using (Form form = MakeDialog("Edit remote profile", 640, 620))
            {
                form.MinimumSize = new Size(600, 540);

                TableLayoutPanel root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.ColumnCount = 1;
                root.RowCount = 3;
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Padding = new Padding(14);
                root.BackColor = form.BackColor;

                Label title = new Label();
                title.Text = "Remote profile";
                title.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
                title.AutoSize = true;
                title.ForeColor = WindowTheme.FgColor;
                title.Margin = new Padding(0, 0, 0, 14);

                // GUI-5 (v0.15.0): four-tab layout — General / Bandwidth /
                // Schedule / Watch — instead of one tall scrolling form.
                // The field controls below are built unchanged; only the
                // wrapping container moves from a flowing column to a
                // TabControl, and each existing GroupBox lands in its own
                // TabPage at the bottom of this method. The dialog's Save
                // path still references the same field locals so the read-
                // back / round-trip logic is identical.
                TabControl tabs = new TabControl();
                tabs.Dock = DockStyle.Fill;
                tabs.Padding = new Point(form.LogicalToDeviceUnits(12), form.LogicalToDeviceUnits(6));

                // Core fields ------------------------------------------------
                TableLayoutPanel grid = new TableLayoutPanel();
                grid.AutoSize = true;
                grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                grid.ColumnCount = 2;
                grid.RowCount = 6;
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                grid.BackColor = form.BackColor;
                grid.Margin = new Padding(0, 0, 0, 8);
                grid.MinimumSize = new Size(560, 0);

                TextBox labelBox = new TextBox(); labelBox.Dock = DockStyle.Fill; labelBox.Text = p.Label;
                TextBox providerBox = new TextBox(); providerBox.Dock = DockStyle.Fill; providerBox.Text = p.Provider;
                TextBox remoteBox = new TextBox(); remoteBox.Dock = DockStyle.Fill; remoteBox.Text = p.Remote;
                TextBox driveBox = new TextBox(); driveBox.Width = form.LogicalToDeviceUnits(90); driveBox.Text = p.DriveLetter;

                CheckBox networkBox = new CheckBox();
                networkBox.AutoSize = true;
                networkBox.Text = "Mount as network drive";
                networkBox.Checked = String.Equals(p.MountMode, "network", StringComparison.OrdinalIgnoreCase);
                networkBox.ForeColor = WindowTheme.FgColor;
                networkBox.Margin = new Padding(0, 8, 0, 4);

                CheckBox autoBox = new CheckBox();
                autoBox.AutoSize = true;
                autoBox.Text = "Auto-mount this profile at Pixelpipe startup";
                autoBox.Checked = p.AutoMount;
                autoBox.ForeColor = WindowTheme.FgColor;
                autoBox.Margin = new Padding(0, 4, 0, 0);

                AddEditRow(grid, 0, "Label", labelBox);
                AddEditRow(grid, 1, "Provider", providerBox);
                AddEditRow(grid, 2, "rclone remote", remoteBox);
                AddEditRow(grid, 3, "Drive letter", driveBox);
                grid.Controls.Add(networkBox, 0, 4);
                grid.SetColumnSpan(networkBox, 2);
                grid.Controls.Add(autoBox, 0, 5);
                grid.SetColumnSpan(autoBox, 2);

                // Bandwidth group --------------------------------------------
                GroupBox bwGroup = new GroupBox();
                bwGroup.Text = "Bandwidth limit (this profile)";
                bwGroup.ForeColor = WindowTheme.FgColor;
                bwGroup.AutoSize = true;
                bwGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                bwGroup.Padding = new Padding(10, 6, 10, 10);
                bwGroup.Margin = new Padding(0, 8, 0, 8);
                bwGroup.MinimumSize = new Size(560, 0);

                string[] bwChoices = new string[] { "", "off", "512K", "1M", "5M", "10M", "25M", "50M", "100M", "250M" };
                ComboBox bwCombo = new ComboBox();
                bwCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                bwCombo.Width = form.LogicalToDeviceUnits(320);
                bwCombo.BackColor = WindowTheme.InputBg;
                bwCombo.ForeColor = WindowTheme.FgColor;
                bwCombo.Margin = new Padding(0, 8, 0, 0);
                for (int i = 0; i < bwChoices.Length; i++)
                {
                    string c = bwChoices[i];
                    bwCombo.Items.Add(c == "" ? "(inherit global: " + DisplayLimit(selectedBandwidth) + ")"
                                              : (c == "off" ? "Unlimited" : c + "/s"));
                }
                int bwInitial = Array.FindIndex(bwChoices, c => String.Equals(c, p.BandwidthLimit ?? "", StringComparison.OrdinalIgnoreCase));
                bwCombo.SelectedIndex = bwInitial >= 0 ? bwInitial : 0;

                Label bwScheduleLabel = new Label();
                bwScheduleLabel.AutoSize = true;
                bwScheduleLabel.Text = "Schedule (overrides the limit above):";
                bwScheduleLabel.ForeColor = WindowTheme.FgColor;
                bwScheduleLabel.Margin = new Padding(0, 14, 0, 4);

                TextBox bwScheduleBox = new TextBox();
                bwScheduleBox.Width = form.LogicalToDeviceUnits(540);
                bwScheduleBox.Text = p.BandwidthScheduleEntries ?? "";
                bwScheduleBox.Margin = new Padding(0, 0, 0, 4);

                Label bwScheduleHint = new Label();
                bwScheduleHint.AutoSize = true;
                bwScheduleHint.MaximumSize = new Size(540, 0);
                bwScheduleHint.Text = "Comma-separated HH:mm=limit entries, e.g. \"00:00=off,09:00=1M,18:00=off\". Valid limits: off, 512K, 1M, 5M, 10M, 25M, 50M, 100M, 250M (or a custom value like \"1.5G\"). Empty disables the bandwidth schedule.";
                bwScheduleHint.ForeColor = WindowTheme.MutedColor;
                bwScheduleHint.Font = new Font("Segoe UI", 8.5f);
                bwScheduleHint.Margin = new Padding(0, 0, 0, 4);

                FlowLayoutPanel bwStack = new FlowLayoutPanel();
                bwStack.FlowDirection = FlowDirection.TopDown;
                bwStack.WrapContents = false;
                bwStack.AutoSize = true;
                bwStack.Dock = DockStyle.Top;
                bwStack.Controls.Add(bwCombo);
                bwStack.Controls.Add(bwScheduleLabel);
                bwStack.Controls.Add(bwScheduleBox);
                bwStack.Controls.Add(bwScheduleHint);
                bwGroup.Controls.Add(bwStack);

                // Schedule group ---------------------------------------------
                GroupBox schedGroup = new GroupBox();
                schedGroup.Text = "Scheduled mount / unmount";
                schedGroup.ForeColor = WindowTheme.FgColor;
                schedGroup.AutoSize = true;
                schedGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                schedGroup.Padding = new Padding(10, 6, 10, 10);
                schedGroup.Margin = new Padding(0, 0, 0, 8);
                schedGroup.MinimumSize = new Size(560, 0);

                TableLayoutPanel schedGrid = new TableLayoutPanel();
                schedGrid.AutoSize = true;
                schedGrid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                schedGrid.ColumnCount = 2;
                schedGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                schedGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                schedGrid.Dock = DockStyle.Top;

                CheckBox schedEnabled = new CheckBox();
                schedEnabled.AutoSize = true;
                schedEnabled.Text = "Enable schedule";
                schedEnabled.Checked = p.ScheduleEnabled;
                schedEnabled.ForeColor = WindowTheme.FgColor;
                schedEnabled.Margin = new Padding(0, 8, 0, 8);

                TextBox mountTimeBox = new TextBox();
                mountTimeBox.Width = form.LogicalToDeviceUnits(100);
                mountTimeBox.Text = p.ScheduleMountTime ?? "";

                TextBox unmountTimeBox = new TextBox();
                unmountTimeBox.Width = form.LogicalToDeviceUnits(100);
                unmountTimeBox.Text = p.ScheduleUnmountTime ?? "";

                FlowLayoutPanel daysRow = new FlowLayoutPanel();
                daysRow.AutoSize = true;
                daysRow.FlowDirection = FlowDirection.LeftToRight;
                daysRow.WrapContents = true;
                daysRow.Margin = new Padding(0, 4, 0, 0);

                string[] dayKeys = new string[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
                HashSet<string> initiallyOn = ParseDayList(p.ScheduleDays);
                Dictionary<string, CheckBox> dayChecks = new Dictionary<string, CheckBox>();
                for (int i = 0; i < dayKeys.Length; i++)
                {
                    CheckBox cb = new CheckBox();
                    cb.AutoSize = true;
                    cb.Text = dayKeys[i];
                    cb.Checked = initiallyOn.Contains(dayKeys[i]);
                    cb.ForeColor = WindowTheme.FgColor;
                    cb.Margin = new Padding(0, 0, 8, 0);
                    dayChecks[dayKeys[i]] = cb;
                    daysRow.Controls.Add(cb);
                }

                AddEditRow(schedGrid, 0, "Mount at (HH:mm)", mountTimeBox);
                AddEditRow(schedGrid, 1, "Unmount at (HH:mm)", unmountTimeBox);
                schedGrid.Controls.Add(schedEnabled, 0, 2);
                schedGrid.SetColumnSpan(schedEnabled, 2);

                FlowLayoutPanel schedStack = new FlowLayoutPanel();
                schedStack.FlowDirection = FlowDirection.TopDown;
                schedStack.WrapContents = false;
                schedStack.AutoSize = true;
                schedStack.Dock = DockStyle.Top;
                schedStack.Controls.Add(schedGrid);
                Label daysLabel = new Label();
                daysLabel.Text = "Days:";
                daysLabel.ForeColor = WindowTheme.FgColor;
                daysLabel.AutoSize = true;
                daysLabel.Margin = new Padding(0, 6, 0, 0);
                schedStack.Controls.Add(daysLabel);
                schedStack.Controls.Add(daysRow);
                schedGroup.Controls.Add(schedStack);

                // Watch folder group ----------------------------------------
                GroupBox watchGroup = new GroupBox();
                watchGroup.Text = "Watch folder (auto-upload)";
                watchGroup.ForeColor = WindowTheme.FgColor;
                watchGroup.AutoSize = true;
                watchGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                watchGroup.Padding = new Padding(10, 6, 10, 10);
                watchGroup.Margin = new Padding(0, 0, 0, 8);
                watchGroup.MinimumSize = new Size(560, 0);

                TableLayoutPanel watchGrid = new TableLayoutPanel();
                watchGrid.AutoSize = true;
                watchGrid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                watchGrid.ColumnCount = 2;
                watchGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                watchGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                watchGrid.Dock = DockStyle.Top;

                CheckBox watchEnabled = new CheckBox();
                watchEnabled.AutoSize = true;
                watchEnabled.Text = "Enable watch folder";
                watchEnabled.Checked = p.WatchFolderEnabled;
                watchEnabled.ForeColor = WindowTheme.FgColor;
                watchEnabled.Margin = new Padding(0, 6, 0, 6);

                TextBox watchPathBox = new TextBox();
                watchPathBox.Width = form.LogicalToDeviceUnits(380);
                watchPathBox.Text = p.WatchFolderPath ?? "";

                Button watchPathBrowse = MakeDialogButton("Browse...", DialogResult.None);
                watchPathBrowse.AutoSize = true;
                watchPathBrowse.Click += delegate
                {
                    using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                    {
                        fbd.Description = "Folder to watch for files to upload";
                        if (!String.IsNullOrWhiteSpace(watchPathBox.Text)) fbd.SelectedPath = watchPathBox.Text;
                        if (fbd.ShowDialog() == DialogResult.OK) watchPathBox.Text = fbd.SelectedPath;
                    }
                };

                FlowLayoutPanel watchPathRow = new FlowLayoutPanel();
                watchPathRow.AutoSize = true;
                watchPathRow.FlowDirection = FlowDirection.LeftToRight;
                watchPathRow.WrapContents = false;
                watchPathRow.Margin = new Padding(0);
                watchPathRow.Controls.Add(watchPathBox);
                watchPathRow.Controls.Add(watchPathBrowse);

                TextBox watchTargetBox = new TextBox();
                watchTargetBox.Width = form.LogicalToDeviceUnits(380);
                watchTargetBox.Text = p.WatchFolderTargetDir ?? "";

                ComboBox watchModeCombo = new ComboBox();
                watchModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                watchModeCombo.Width = form.LogicalToDeviceUnits(180);
                watchModeCombo.BackColor = WindowTheme.InputBg;
                watchModeCombo.ForeColor = WindowTheme.FgColor;
                watchModeCombo.Items.Add("move (delete local after upload)");
                watchModeCombo.Items.Add("copy (keep local)");
                watchModeCombo.SelectedIndex = NormalizeWatchMode(p.WatchFolderMode) == "copy" ? 1 : 0;

                TextBox watchQuietBox = new TextBox();
                watchQuietBox.Width = form.LogicalToDeviceUnits(100);
                watchQuietBox.Text = (p.WatchFolderQuietMs > 0 ? p.WatchFolderQuietMs : 5000).ToString();

                AddEditRow(watchGrid, 0, "Watch folder path", watchPathRow);
                AddEditRow(watchGrid, 1, "Remote subdir (optional)", watchTargetBox);
                AddEditRow(watchGrid, 2, "Mode", watchModeCombo);
                AddEditRow(watchGrid, 3, "Quiet period (ms)", watchQuietBox);
                watchGrid.Controls.Add(watchEnabled, 0, 4);
                watchGrid.SetColumnSpan(watchEnabled, 2);

                Label watchHelp = new Label();
                watchHelp.AutoSize = true;
                watchHelp.MaximumSize = new Size(540, 0);
                watchHelp.Text = "When enabled, Pixelpipe watches the folder for new files. After the quiet period passes without a write, the file is uploaded via rclone. Failed uploads retry with 30s / 2m / 10m backoff (3 attempts then drop).";
                watchHelp.ForeColor = WindowTheme.MutedColor;
                watchHelp.Font = new Font("Segoe UI", 8.5f);
                watchHelp.Margin = new Padding(0, 6, 0, 0);

                FlowLayoutPanel watchStack = new FlowLayoutPanel();
                watchStack.FlowDirection = FlowDirection.TopDown;
                watchStack.WrapContents = false;
                watchStack.AutoSize = true;
                watchStack.Dock = DockStyle.Top;
                watchStack.Controls.Add(watchGrid);
                watchStack.Controls.Add(watchHelp);
                watchGroup.Controls.Add(watchStack);

                // Each tab gets one of the existing group panels. Inner
                // GroupBox titles stay so the visual border still reads,
                // but the tab title is what the user uses to navigate.
                TabPage generalPage = new TabPage("General"); generalPage.BackColor = form.BackColor; generalPage.ForeColor = WindowTheme.FgColor; generalPage.Padding = new Padding(12); generalPage.Controls.Add(grid);
                TabPage bwPage = new TabPage("Bandwidth"); bwPage.BackColor = form.BackColor; bwPage.ForeColor = WindowTheme.FgColor; bwPage.Padding = new Padding(12); bwPage.Controls.Add(bwGroup);
                TabPage schedPage = new TabPage("Schedule"); schedPage.BackColor = form.BackColor; schedPage.ForeColor = WindowTheme.FgColor; schedPage.Padding = new Padding(12); schedPage.Controls.Add(schedGroup);
                TabPage watchPage = new TabPage("Watch"); watchPage.BackColor = form.BackColor; watchPage.ForeColor = WindowTheme.FgColor; watchPage.Padding = new Padding(12); watchPage.Controls.Add(watchGroup);
                tabs.TabPages.Add(generalPage);
                tabs.TabPages.Add(bwPage);
                tabs.TabPages.Add(schedPage);
                tabs.TabPages.Add(watchPage);

                FlowLayoutPanel footer = new FlowLayoutPanel();
                footer.Dock = DockStyle.Fill;
                footer.AutoSize = true;
                footer.FlowDirection = FlowDirection.RightToLeft;
                footer.WrapContents = false;
                footer.Margin = new Padding(0, 14, 0, 0);

                Button cancel = MakeDialogButton("Cancel", DialogResult.Cancel);
                Button save = MakeDialogButton("Save", DialogResult.OK);
                footer.Controls.Add(cancel);
                footer.Controls.Add(save);

                root.Controls.Add(title, 0, 0);
                root.Controls.Add(tabs, 0, 1);
                root.Controls.Add(footer, 0, 2);
                form.Controls.Add(root);
                form.AcceptButton = save; form.CancelButton = cancel;
                if (form.ShowDialog() == DialogResult.OK)
                {
                    if (IsMounted(p)) { MessageBox.Show("Unmount this profile before editing it.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                    p.Label = String.IsNullOrWhiteSpace(labelBox.Text) ? RemoteNameBare(remoteBox.Text) : labelBox.Text.Trim();
                    p.Provider = NormalizeProvider(providerBox.Text, remoteBox.Text);
                    p.Remote = NormalizeRemoteName(remoteBox.Text);
                    p.DriveLetter = NormalizeDriveLetter(driveBox.Text);
                    p.MountMode = networkBox.Checked ? "network" : "fixed";
                    p.AutoMount = autoBox.Checked;

                    int bwIdx = bwCombo.SelectedIndex;
                    p.BandwidthLimit = (bwIdx > 0 && bwIdx < bwChoices.Length) ? bwChoices[bwIdx] : "";
                    // Trim and round-trip through the parser so any invalid
                    // tokens get dropped at save time instead of saved-and-
                    // ignored-at-runtime, which would surprise the user.
                    string bwSchedRaw = (bwScheduleBox.Text ?? "").Trim();
                    List<BandwidthScheduleEntry> parsedSched = ParseBandwidthSchedule(bwSchedRaw);
                    if (parsedSched.Count == 0)
                    {
                        p.BandwidthScheduleEntries = "";
                    }
                    else
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < parsedSched.Count; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append(parsedSched[i].Time);
                            sb.Append("=");
                            sb.Append(parsedSched[i].Limit);
                        }
                        p.BandwidthScheduleEntries = sb.ToString();
                    }
                    p.LastBandwidthScheduleKey = null;

                    string normMount, normUnmount;
                    p.ScheduleMountTime = TryNormalizeScheduleTime(mountTimeBox.Text, out normMount) ? normMount : "";
                    p.ScheduleUnmountTime = TryNormalizeScheduleTime(unmountTimeBox.Text, out normUnmount) ? normUnmount : "";
                    StringBuilder days = new StringBuilder();
                    for (int i = 0; i < dayKeys.Length; i++)
                    {
                        if (dayChecks[dayKeys[i]].Checked)
                        {
                            if (days.Length > 0) days.Append(',');
                            days.Append(dayKeys[i]);
                        }
                    }
                    p.ScheduleDays = days.Length == 0 ? "Mon,Tue,Wed,Thu,Fri,Sat,Sun" : days.ToString();
                    p.ScheduleEnabled = schedEnabled.Checked && (!String.IsNullOrEmpty(p.ScheduleMountTime) || !String.IsNullOrEmpty(p.ScheduleUnmountTime));
                    // Reset throttling so the new schedule fires on its very next window.
                    p.LastScheduleMountKey = null;
                    p.LastScheduleUnmountKey = null;

                    p.WatchFolderEnabled = watchEnabled.Checked;
                    p.WatchFolderPath = (watchPathBox.Text ?? "").Trim();
                    p.WatchFolderTargetDir = (watchTargetBox.Text ?? "").Trim();
                    p.WatchFolderMode = watchModeCombo.SelectedIndex == 1 ? "copy" : "move";
                    int parsedQuiet;
                    p.WatchFolderQuietMs = (Int32.TryParse((watchQuietBox.Text ?? "").Trim(), out parsedQuiet) && parsedQuiet >= 500 && parsedQuiet <= 600000)
                        ? parsedQuiet
                        : 5000;
                    if (p.WatchFolderEnabled && (String.IsNullOrEmpty(p.WatchFolderPath) || !System.IO.Directory.Exists(p.WatchFolderPath)))
                    {
                        MessageBox.Show("Watch folder is enabled but the path is empty or does not exist. The folder will not be watched until you fix the path.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }

                    AssignRuntimeFields();
                    SaveProfiles();
                    RebuildMenu();
                    UpdateMainWindowLiveState();
                    ShowBalloon("Profile saved.");
                }
            }
        }

        // Parses "Mon,Wed,Fri" into a case-insensitive set of canonical
        // abbreviations. Defaults to all seven days for empty/null input so
        // older profiles without ScheduleDays keep their pre-v0.8 behaviour.
        private static HashSet<string> ParseDayList(string list)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] canonical = new string[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            if (String.IsNullOrWhiteSpace(list))
            {
                for (int i = 0; i < canonical.Length; i++) set.Add(canonical[i]);
                return set;
            }
            string[] parts = list.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                for (int j = 0; j < canonical.Length; j++)
                {
                    if (String.Equals(token, canonical[j], StringComparison.OrdinalIgnoreCase)) set.Add(canonical[j]);
                }
            }
            return set;
        }

        private void AddEditRow(TableLayoutPanel grid, int row, string labelText, Control editor)
        {
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.ForeColor = WindowTheme.FgColor;
            label.Margin = new Padding(0, 0, 18, 10);

            editor.Margin = new Padding(0, 0, 0, 10);
            editor.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            grid.Controls.Add(label, 0, row);
            grid.Controls.Add(editor, 1, row);
        }

        private void ToggleProfileAutoMount(RemoteProfile p)
        {
            if (p == null) return;
            p.AutoMount = !p.AutoMount;
            SaveProfiles();
            RebuildMenu();
            ShowBalloon(p.Label + (p.AutoMount ? " will auto-mount." : " will not auto-mount."));
        }

        private void RemoveProfile(RemoteProfile p)
        {
            if (p == null || IsMounted(p)) return;
            DialogResult r = MessageBox.Show("Remove Pixelpipe profile for " + p.Label + "?\r\n\r\nThis does not delete the underlying rclone remote.\r\n\r\nA timestamped backup of settings.json is kept in the backups folder; you can restore it from Tools / diagnostics → Open settings backups folder.", "Pixelpipe", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            BackupSettingsFile("remove-" + SafeFileName(p.Label));
            lock (profilesLock)
            {
                profiles.Remove(p);
                if (profiles.Count == 0) profiles.Add(new RemoteProfile());
            }
            AssignRuntimeFields();
            SaveProfiles();
            RebuildMenu();
        }

        // Open the main window on Profiles. The legacy ListView-in-modal version used
        // hardcoded pixel positions and would clip its action row at different
        // font/DPI sizes the same way the diagnostics modal did. The Profiles tab
        // already shows every profile as a card with all the same actions (Edit,
        // Set primary, Auto-mount toggle, Remove, plus Add cloud remote ▾ and
        // Import existing) so the modal is redundant.
        private void ShowManageRemotesWindow()
        {
            ShowMainWindow("Profiles");
        }

    }
}
