using System;
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

        private void AddGuidedRcloneRemote(string label, string provider, string preferredDrive)
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet. Install rclone first, then add this remote.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string remoteName = PromptForValue("Add " + label, "Remote name to create/use in rclone:", UniqueRemoteName(label));
            if (remoteName == null) return;
            remoteName = RemoteNameBare(remoteName.Trim());
            if (remoteName.Length == 0) return;

            string drive = PromptForValue("Drive letter", "Drive letter for this remote:", FirstFreePreferredDrive(preferredDrive));
            if (drive == null) return;

            RemoteProfile p = new RemoteProfile();
            p.Label = label;
            p.Provider = provider;
            p.Remote = NormalizeRemoteName(remoteName);
            p.DriveLetter = NormalizeDriveLetter(drive);
            p.MountMode = "network";
            lock (profilesLock) profiles.Add(p);
            AssignRuntimeFields();
            SaveProfiles();
            RebuildMenu();

            if (!RemoteConfigured(p))
            {
                StringBuilder msg = new StringBuilder();
                msg.AppendLine(label + " has been added to Pixelpipe, but the rclone remote does not exist yet.");
                msg.AppendLine();
                msg.AppendLine("Pixelpipe will open rclone config. Create a remote named:");
                msg.AppendLine(p.Remote);
                msg.AppendLine();
                msg.AppendLine("Choose backend/provider:");
                msg.AppendLine(provider);
                msg.AppendLine();
                msg.AppendLine("After rclone config is complete, return to Pixelpipe and mount it from the tray.");
                MessageBox.Show(msg.ToString(), "Pixelpipe guided remote setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OpenRcloneConfigTerminal();
            }
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
            string result = RunRcloneCapture("config create " + QuoteArg(bare) + " pixeldrain api_key " + QuoteArg(apiKey) + " root_folder_id me --non-interactive", 15000);
            if (result.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 || result.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result = RunRcloneCapture("config create " + QuoteArg(bare) + " pixeldrain api_key " + QuoteArg(apiKey) + " directory_id me --non-interactive", 15000);
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
                LogUiWarn("configure remote", "rclone did not report " + p.Remote + " after config create; raw output: " + result);
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
            using (Form form = MakeDialog("Edit remote profile", 600, 390))
            {
                form.MinimumSize = new Size(560, 360);

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

                TableLayoutPanel grid = new TableLayoutPanel();
                grid.Dock = DockStyle.Fill;
                grid.AutoSize = true;
                grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                grid.ColumnCount = 2;
                grid.RowCount = 6;
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                grid.BackColor = form.BackColor;

                TextBox labelBox = new TextBox();
                labelBox.Dock = DockStyle.Fill;
                labelBox.Text = p.Label;

                TextBox providerBox = new TextBox();
                providerBox.Dock = DockStyle.Fill;
                providerBox.Text = p.Provider;

                TextBox remoteBox = new TextBox();
                remoteBox.Dock = DockStyle.Fill;
                remoteBox.Text = p.Remote;

                TextBox driveBox = new TextBox();
                driveBox.Width = 90;
                driveBox.Text = p.DriveLetter;

                CheckBox networkBox = new CheckBox();
                networkBox.AutoSize = true;
                networkBox.Text = "Mount as network drive";
                networkBox.Checked = String.Equals(p.MountMode, "network", StringComparison.OrdinalIgnoreCase);
                networkBox.ForeColor = WindowTheme.FgColor;
                networkBox.Margin = new Padding(0, 8, 0, 4);

                CheckBox autoBox = new CheckBox();
                autoBox.AutoSize = true;
                autoBox.Text = "Auto-mount this profile at startup";
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
                root.Controls.Add(grid, 0, 1);
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
                    AssignRuntimeFields();
                    SaveProfiles();
                    RebuildMenu();
                    ShowBalloon("Profile saved.");
                }
            }
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
            DialogResult r = MessageBox.Show("Remove Pixelpipe profile for " + p.Label + "?\r\n\r\nThis does not delete the underlying rclone remote.", "Pixelpipe", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
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
