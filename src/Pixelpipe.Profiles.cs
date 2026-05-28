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
            for (int i = 0; i < profiles.Count; i++)
            {
                if (String.Equals(NormalizeRemoteName(profiles[i].Remote), n, StringComparison.OrdinalIgnoreCase)) return true;
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
            lock (profilesLock)
            {
                if (profiles.Count == 0)
                {
                    profiles.Add(new RemoteProfile());
                }
            }
            if (profiles.Count > 0 && String.IsNullOrWhiteSpace(profiles[0].Id))
            {
                AssignRuntimeFields();
            }
            return profiles[0];
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
            using (Form form = MakeDialog("Edit remote profile", 560, 360))
            using (Label title = new Label())
            using (Label labelL = new Label())
            using (TextBox labelBox = new TextBox())
            using (Label providerL = new Label())
            using (TextBox providerBox = new TextBox())
            using (Label remoteL = new Label())
            using (TextBox remoteBox = new TextBox())
            using (Label driveL = new Label())
            using (TextBox driveBox = new TextBox())
            using (CheckBox networkBox = new CheckBox())
            using (CheckBox autoBox = new CheckBox())
            using (Button save = new Button())
            using (Button cancel = new Button())
            {
                title.Text = "Remote profile"; title.Font = new Font("Segoe UI", 13f, FontStyle.Bold); title.Left = 14; title.Top = 14; title.Width = 480; title.Height = 30; title.ForeColor = Color.WhiteSmoke;
                labelL.Text = "Label"; labelL.Left = 14; labelL.Top = 58; labelL.Width = 150; labelL.ForeColor = Color.WhiteSmoke;
                labelBox.Left = 170; labelBox.Top = 54; labelBox.Width = 340; labelBox.Text = p.Label;
                providerL.Text = "Provider"; providerL.Left = 14; providerL.Top = 92; providerL.Width = 150; providerL.ForeColor = Color.WhiteSmoke;
                providerBox.Left = 170; providerBox.Top = 88; providerBox.Width = 340; providerBox.Text = p.Provider;
                remoteL.Text = "rclone remote"; remoteL.Left = 14; remoteL.Top = 126; remoteL.Width = 150; remoteL.ForeColor = Color.WhiteSmoke;
                remoteBox.Left = 170; remoteBox.Top = 122; remoteBox.Width = 340; remoteBox.Text = p.Remote;
                driveL.Text = "Drive letter"; driveL.Left = 14; driveL.Top = 160; driveL.Width = 150; driveL.ForeColor = Color.WhiteSmoke;
                driveBox.Left = 170; driveBox.Top = 156; driveBox.Width = 80; driveBox.Text = p.DriveLetter;
                networkBox.Text = "Mount as network drive"; networkBox.Left = 14; networkBox.Top = 198; networkBox.Width = 470; networkBox.Checked = String.Equals(p.MountMode, "network", StringComparison.OrdinalIgnoreCase); networkBox.ForeColor = Color.WhiteSmoke;
                autoBox.Text = "Auto-mount this profile at startup"; autoBox.Left = 14; autoBox.Top = 228; autoBox.Width = 470; autoBox.Checked = p.AutoMount; autoBox.ForeColor = Color.WhiteSmoke;
                save.Text = "Save"; save.Left = 334; save.Top = 276; save.Width = 84; save.DialogResult = DialogResult.OK;
                cancel.Text = "Cancel"; cancel.Left = 426; cancel.Top = 276; cancel.Width = 84; cancel.DialogResult = DialogResult.Cancel;
                form.Controls.Add(title); form.Controls.Add(labelL); form.Controls.Add(labelBox); form.Controls.Add(providerL); form.Controls.Add(providerBox); form.Controls.Add(remoteL); form.Controls.Add(remoteBox); form.Controls.Add(driveL); form.Controls.Add(driveBox); form.Controls.Add(networkBox); form.Controls.Add(autoBox); form.Controls.Add(save); form.Controls.Add(cancel);
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

        private void ShowManageRemotesWindow()
        {
            Form form = new Form();
            form.Text = "Pixelpipe remote profiles";
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Width = 780;
            form.Height = 520;
            form.BackColor = Color.FromArgb(18, 22, 28);
            form.ForeColor = Color.WhiteSmoke;
            ListView list = new ListView();
            list.View = View.Details;
            list.FullRowSelect = true;
            list.Left = 12; list.Top = 12; list.Width = 740; list.Height = 360;
            list.Columns.Add("Label", 160); list.Columns.Add("Provider", 110); list.Columns.Add("Remote", 170); list.Columns.Add("Drive", 60); list.Columns.Add("Mode", 90); list.Columns.Add("Startup", 70); list.Columns.Add("Status", 130);
            list.BackColor = Color.FromArgb(14, 18, 24); list.ForeColor = Color.WhiteSmoke;
            for (int i = 0; i < profiles.Count; i++)
            {
                RemoteProfile p = profiles[i];
                ListViewItem item = new ListViewItem(p.Label);
                item.SubItems.Add(DisplayProvider(p.Provider));
                item.SubItems.Add(p.Remote);
                item.SubItems.Add(p.DriveLetter);
                item.SubItems.Add(p.MountMode);
                item.SubItems.Add(p.AutoMount ? "yes" : "no");
                item.SubItems.Add(p.StatusText);
                item.Tag = p;
                list.Items.Add(item);
            }
            Button add = new Button(); add.Text = "Add existing"; add.Left = 12; add.Top = 392; add.Width = 110; add.Click += delegate { AddExistingRemoteProfile(); form.Close(); };
            Button import = new Button(); import.Text = "Import remotes"; import.Left = 130; import.Top = 392; import.Width = 120; import.Click += delegate { ImportExistingRemotes(); form.Close(); };
            Button edit = new Button(); edit.Text = "Edit selected"; edit.Left = 258; edit.Top = 392; edit.Width = 120; edit.Click += delegate { if (list.SelectedItems.Count > 0) { EditProfile((RemoteProfile)list.SelectedItems[0].Tag); form.Close(); } };
            Button primary = new Button(); primary.Text = "Set primary"; primary.Left = 386; primary.Top = 392; primary.Width = 110; primary.Click += delegate { if (list.SelectedItems.Count > 0) { MakePrimaryProfile((RemoteProfile)list.SelectedItems[0].Tag); form.Close(); } };
            Button close = new Button(); close.Text = "Close"; close.Left = 662; close.Top = 432; close.Width = 90; close.Click += delegate { form.Close(); };
            form.Controls.Add(list); form.Controls.Add(add); form.Controls.Add(import); form.Controls.Add(edit); form.Controls.Add(primary); form.Controls.Add(close);
            form.FormClosed += delegate { form.Dispose(); };
            form.Show();
        }
    }
}
