using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        // Field spec for ShowProviderForm. `Key` is the rclone config field name
        // passed to `rclone config create` (e.g. "access_key_id"). `Label` is
        // what the user sees. `IsPassword` masks the textbox and tells rclone
        // we mean a password (it obfuscates it internally). `Required` makes
        // the OK button refuse an empty value. `Default` pre-fills the field.
        internal sealed class ProviderField
        {
            public string Key;
            public string Label;
            public string Default;
            public string Help;
            public bool IsPassword;
            public bool Required;
            public string[] Choices; // when non-null, render as a ComboBox

            public ProviderField(string key, string label, string def, string help, bool isPassword, bool required, string[] choices)
            {
                Key = key;
                Label = label;
                Default = def ?? "";
                Help = help ?? "";
                IsPassword = isPassword;
                Required = required;
                Choices = choices;
            }
        }

        // Build the `--non-interactive` argument string for `rclone config create`.
        // Pure helper so tests can verify quoting and field ordering without
        // talking to rclone. Returns: "config create <quotedName> <type> k1 v1 k2 v2 ... --non-interactive"
        internal static string BuildRcloneConfigCreateArgs(string remoteName, string rcloneType, List<KeyValuePair<string, string>> fields)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("config create ");
            sb.Append(QuoteArg(remoteName));
            sb.Append(' ');
            sb.Append(rcloneType);
            if (fields != null)
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    KeyValuePair<string, string> kv = fields[i];
                    if (String.IsNullOrEmpty(kv.Key)) continue;
                    sb.Append(' ');
                    sb.Append(kv.Key);
                    sb.Append(' ');
                    sb.Append(QuoteArg(kv.Value ?? ""));
                }
            }
            sb.Append(" --non-interactive");
            return sb.ToString();
        }

        // Generic per-field input dialog. Builds a labeled form, validates required
        // fields, returns the populated values keyed by field key. Returns null
        // if the user cancels. Used by every non-Pixeldrain provider wizard.
        private Dictionary<string, string> ShowProviderForm(string title, string intro, List<ProviderField> fields)
        {
            if (fields == null || fields.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int height = Math.Max(220, 130 + (fields.Count * 56));
            using (Form form = MakeDialog(title, 600, height))
            {
                form.MinimumSize = new Size(560, 320);
                form.FormBorderStyle = FormBorderStyle.Sizable;

                TableLayoutPanel root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.ColumnCount = 1;
                root.RowCount = 3;
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Padding = new Padding(14);
                root.BackColor = form.BackColor;

                Label header = new Label();
                header.AutoSize = true;
                header.MaximumSize = new Size(560, 0);
                header.Text = intro ?? "";
                header.ForeColor = WindowTheme.FgColor;
                header.Margin = new Padding(0, 0, 0, 12);
                root.Controls.Add(header, 0, 0);

                FlowLayoutPanel body = new FlowLayoutPanel();
                body.Dock = DockStyle.Fill;
                body.FlowDirection = FlowDirection.TopDown;
                body.WrapContents = false;
                body.AutoScroll = true;
                body.BackColor = form.BackColor;

                Dictionary<string, Control> editors = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < fields.Count; i++)
                {
                    ProviderField f = fields[i];
                    Label label = new Label();
                    label.AutoSize = true;
                    label.Text = f.Label + (f.Required ? " *" : "");
                    label.ForeColor = WindowTheme.FgColor;
                    label.Margin = new Padding(0, 8, 0, 2);
                    body.Controls.Add(label);

                    Control editor;
                    if (f.Choices != null && f.Choices.Length > 0)
                    {
                        ComboBox combo = new ComboBox();
                        combo.DropDownStyle = ComboBoxStyle.DropDownList;
                        combo.Width = form.LogicalToDeviceUnits(540);
                        combo.BackColor = WindowTheme.InputBg;
                        combo.ForeColor = WindowTheme.FgColor;
                        combo.Margin = new Padding(0, 0, 0, 2);
                        for (int j = 0; j < f.Choices.Length; j++) combo.Items.Add(f.Choices[j]);
                        int initialIdx = Array.FindIndex(f.Choices, c => String.Equals(c, f.Default, StringComparison.OrdinalIgnoreCase));
                        combo.SelectedIndex = initialIdx >= 0 ? initialIdx : 0;
                        editor = combo;
                    }
                    else
                    {
                        TextBox tb = new TextBox();
                        tb.Width = form.LogicalToDeviceUnits(540);
                        tb.BackColor = WindowTheme.InputBg;
                        tb.ForeColor = WindowTheme.FgColor;
                        tb.Margin = new Padding(0, 0, 0, 2);
                        tb.Text = f.Default;
                        if (f.IsPassword) tb.UseSystemPasswordChar = true;
                        editor = tb;
                    }
                    body.Controls.Add(editor);
                    editors[f.Key] = editor;

                    if (!String.IsNullOrEmpty(f.Help))
                    {
                        Label help = new Label();
                        help.AutoSize = true;
                        help.MaximumSize = new Size(540, 0);
                        help.Text = f.Help;
                        help.ForeColor = WindowTheme.MutedColor;
                        help.Font = new Font("Segoe UI", 8.5f);
                        help.Margin = new Padding(0, 0, 0, 4);
                        body.Controls.Add(help);
                    }
                }

                root.Controls.Add(body, 0, 1);

                FlowLayoutPanel footer = new FlowLayoutPanel();
                footer.Dock = DockStyle.Fill;
                footer.AutoSize = true;
                footer.FlowDirection = FlowDirection.RightToLeft;
                footer.WrapContents = false;
                footer.Margin = new Padding(0, 12, 0, 0);

                Button cancel = MakeDialogButton("Cancel", DialogResult.Cancel);
                Button ok = MakeDialogButton("Create remote", DialogResult.None);
                footer.Controls.Add(cancel);
                footer.Controls.Add(ok);

                ok.Click += delegate
                {
                    for (int i = 0; i < fields.Count; i++)
                    {
                        if (!fields[i].Required) continue;
                        string val = ReadEditor(editors[fields[i].Key]).Trim();
                        if (val.Length == 0)
                        {
                            MessageBox.Show("'" + fields[i].Label + "' is required.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };

                root.Controls.Add(footer, 0, 2);
                form.Controls.Add(root);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                if (form.ShowDialog() != DialogResult.OK) return null;

                Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < fields.Count; i++)
                {
                    result[fields[i].Key] = ReadEditor(editors[fields[i].Key]);
                }
                return result;
            }
        }

        private string ReadEditor(Control c)
        {
            TextBox tb = c as TextBox;
            if (tb != null) return tb.Text ?? "";
            ComboBox combo = c as ComboBox;
            if (combo != null) return combo.SelectedItem == null ? "" : combo.SelectedItem.ToString();
            return "";
        }

        // Common back-end for non-OAuth providers. PERF-2 (v0.13.1): the
        // rclone config write + listremotes round-trip now runs on a worker
        // thread so the wizard's OK click returns immediately. Profile
        // creation and the success/failure dialog marshal back via BeginUi.
        // SEC-1 (v0.13.0): secrets pipe through `rclone obscure -` over
        // stdin in WriteRemoteToRcloneConfig, never on argv.
        private void CreateRemoteAndProfile(string label, string providerKey, string preferredDrive, string remoteName, string rcloneType, List<KeyValuePair<string, string>> rcloneFields)
        {
            string bare = RemoteNameBare(remoteName);
            ShowBalloon(label + ": creating remote...");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string writeError = null;
                bool listed = false;
                try
                {
                    writeError = WriteRemoteToRcloneConfig(bare, rcloneType, rcloneFields);
                    listed = (writeError == null) && RemoteListContains(bare);
                }
                catch (Exception ex)
                {
                    LogUiIssue("create remote " + bare, ex);
                    writeError = ex.Message;
                }
                BeginUi(delegate
                {
                    if (writeError != null)
                    {
                        MessageBox.Show(label + " remote could not be written to rclone.conf:\r\n\r\n" + writeError, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        LogUiWarn("provider wizard", label + " config write failed: " + writeError);
                        return;
                    }
                    if (!listed)
                    {
                        MessageBox.Show(label + " remote was written but rclone listremotes does not show it. Check rclone config manually.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        LogUiWarn("provider wizard", label + " listremotes did not show " + bare + " after config write");
                        return;
                    }

                    RemoteProfile p = new RemoteProfile();
                    p.Label = UniqueLabel(label);
                    p.Provider = providerKey;
                    p.Remote = NormalizeRemoteName(bare);
                    p.DriveLetter = FirstFreePreferredDrive(preferredDrive);
                    p.MountMode = "network";
                    lock (profilesLock) profiles.Add(p);
                    AssignRuntimeFields();
                    SaveProfiles();
                    RebuildMenu();
                    RebuildMainWindowProfiles();
                    ShowBalloon("Configured " + label + " remote: " + p.Remote);
                });
            });
        }

        private bool RemoteListContains(string bareName)
        {
            try
            {
                string[] remotes = ListRcloneRemotes();
                string needle = (bareName ?? "").Trim();
                for (int i = 0; i < remotes.Length; i++)
                {
                    if (String.Equals(RemoteNameBare(remotes[i]), needle, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch (Exception ex) { LogUiIssue("remote list contains " + bareName, ex); }
            return false;
        }

        // ------- OAuth providers (Drive, OneDrive, Dropbox, Box) -------
        // OAuth flows need a browser callback; rclone handles that itself via
        // `rclone config`. We can't easily script the browser-redirect dance,
        // so we open the terminal pre-loaded with instructions and ask the
        // user to come back when they're done. Once they confirm the remote
        // exists, we create the profile.
        private void ConfigureOAuthRemoteWizard(string label, string providerKey, string rcloneType, string preferredDrive)
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet. Install rclone first, then add this remote.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string defaultName = UniqueRemoteName(label);
            List<ProviderField> fields = new List<ProviderField>();
            fields.Add(new ProviderField("__name", "Remote name", defaultName, "rclone identifier without the trailing colon.", false, true, null));
            Dictionary<string, string> values = ShowProviderForm(
                "Add " + label,
                label + " uses OAuth. Pixelpipe will open an rclone config terminal where you sign in with your browser.\r\n\r\nWhen rclone says \"All configuration complete\", close the terminal and return here.",
                fields);
            if (values == null) return;

            string remoteName = RemoteNameBare(values["__name"]);
            if (remoteName.Length == 0) return;
            if (RemoteListContains(remoteName))
            {
                MessageBox.Show("A remote named '" + remoteName + "' already exists in rclone. Pick a different name or use 'Import existing rclone remotes'.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            OpenRcloneConfigTerminal();

            DialogResult done = MessageBox.Show(
                "After you finish the rclone config wizard, click OK and Pixelpipe will verify the remote and add a profile.\r\n\r\nClick Cancel to abort.",
                "Pixelpipe — " + label,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (done != DialogResult.OK) return;

            if (!RemoteListContains(remoteName))
            {
                MessageBox.Show("rclone does not list a remote named '" + remoteName + "'. Re-run 'rclone config' if needed, then try Manage remotes... → Import existing.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RemoteProfile p = new RemoteProfile();
            p.Label = UniqueLabel(label);
            p.Provider = providerKey;
            p.Remote = NormalizeRemoteName(remoteName);
            p.DriveLetter = FirstFreePreferredDrive(preferredDrive);
            p.MountMode = "network";
            lock (profilesLock) profiles.Add(p);
            AssignRuntimeFields();
            SaveProfiles();
            RebuildMenu();
            RebuildMainWindowProfiles();
            ShowBalloon("Added " + label + " profile: " + p.Remote);
        }

        // ------- S3 family (AWS S3, Wasabi, Backblaze B2-via-S3, Cloudflare R2, etc.) -------
        private void ConfigureS3RemoteWizard()
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<ProviderField> fields = new List<ProviderField>();
            fields.Add(new ProviderField("__name", "Remote name", UniqueRemoteName("S3"), "rclone identifier without the trailing colon.", false, true, null));
            fields.Add(new ProviderField("provider", "Provider", "AWS", "Pick the bucket provider. rclone tunes endpoints and signing per choice.", false, true,
                new string[] { "AWS", "Wasabi", "Cloudflare", "Backblaze", "DigitalOcean", "Linode", "Storj", "Other" }));
            fields.Add(new ProviderField("access_key_id", "Access key ID", "", "From your provider's IAM / API console.", false, true, null));
            fields.Add(new ProviderField("secret_access_key", "Secret access key", "", "Stored obfuscated by rclone; Pixelpipe never persists it itself.", true, true, null));
            fields.Add(new ProviderField("region", "Region", "us-east-1", "Bucket region. For Wasabi/R2/B2 this is provider-specific (e.g. us-east-1, auto).", false, false, null));
            fields.Add(new ProviderField("endpoint", "Endpoint URL (optional)", "", "Leave empty for AWS. Wasabi/R2/B2/Storj need their own endpoint (e.g. https://s3.wasabisys.com).", false, false, null));
            Dictionary<string, string> values = ShowProviderForm(
                "Add S3-compatible bucket",
                "S3 / R2 / B2 / Wasabi / DigitalOcean / Storj — anything that speaks the S3 API. Fill in your credentials, then Pixelpipe creates the remote via rclone.",
                fields);
            if (values == null) return;

            string remoteName = RemoteNameBare(values["__name"]);
            List<KeyValuePair<string, string>> rcloneFields = new List<KeyValuePair<string, string>>();
            rcloneFields.Add(new KeyValuePair<string, string>("provider", values["provider"]));
            rcloneFields.Add(new KeyValuePair<string, string>("access_key_id", values["access_key_id"]));
            rcloneFields.Add(new KeyValuePair<string, string>("secret_access_key", values["secret_access_key"]));
            if (!String.IsNullOrWhiteSpace(values["region"])) rcloneFields.Add(new KeyValuePair<string, string>("region", values["region"]));
            if (!String.IsNullOrWhiteSpace(values["endpoint"])) rcloneFields.Add(new KeyValuePair<string, string>("endpoint", values["endpoint"]));
            CreateRemoteAndProfile("S3-compatible", "s3", "R:", remoteName, "s3", rcloneFields);
        }

        // ------- WebDAV (Nextcloud, ownCloud, SharePoint, generic) -------
        private void ConfigureWebDAVRemoteWizard()
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<ProviderField> fields = new List<ProviderField>();
            fields.Add(new ProviderField("__name", "Remote name", UniqueRemoteName("WebDAV"), "rclone identifier without the trailing colon.", false, true, null));
            fields.Add(new ProviderField("url", "Server URL", "", "Full WebDAV URL, e.g. https://cloud.example.com/remote.php/dav/files/USERNAME/", false, true, null));
            fields.Add(new ProviderField("vendor", "Vendor", "nextcloud", "Which WebDAV implementation. Pick \"other\" for unknown servers.", false, true,
                new string[] { "nextcloud", "owncloud", "sharepoint", "rclone", "infinitescale", "other" }));
            fields.Add(new ProviderField("user", "Username", "", "Your account username on the WebDAV server.", false, true, null));
            fields.Add(new ProviderField("pass", "Password / app password", "", "Stored obfuscated by rclone. App-passwords recommended where the server supports them.", true, true, null));
            Dictionary<string, string> values = ShowProviderForm(
                "Add WebDAV / Nextcloud",
                "Connect to a Nextcloud, ownCloud, SharePoint, or generic WebDAV server.",
                fields);
            if (values == null) return;

            string remoteName = RemoteNameBare(values["__name"]);
            List<KeyValuePair<string, string>> rcloneFields = new List<KeyValuePair<string, string>>();
            rcloneFields.Add(new KeyValuePair<string, string>("url", values["url"]));
            rcloneFields.Add(new KeyValuePair<string, string>("vendor", values["vendor"]));
            rcloneFields.Add(new KeyValuePair<string, string>("user", values["user"]));
            rcloneFields.Add(new KeyValuePair<string, string>("pass", values["pass"]));
            CreateRemoteAndProfile("WebDAV", "webdav", "W:", remoteName, "webdav", rcloneFields);
        }

        // ------- SFTP -------
        private void ConfigureSFTPRemoteWizard()
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<ProviderField> fields = new List<ProviderField>();
            fields.Add(new ProviderField("__name", "Remote name", UniqueRemoteName("SFTP"), "rclone identifier without the trailing colon.", false, true, null));
            fields.Add(new ProviderField("host", "Host", "", "SSH host (e.g. server.example.com or 10.0.0.5).", false, true, null));
            fields.Add(new ProviderField("port", "Port", "22", "Defaults to 22.", false, false, null));
            fields.Add(new ProviderField("user", "Username", Environment.UserName ?? "", "SSH login user.", false, true, null));
            fields.Add(new ProviderField("pass", "Password", "", "Leave empty if you use key-based auth — Pixelpipe will fall back to your SSH agent / default key.", true, false, null));
            Dictionary<string, string> values = ShowProviderForm(
                "Add SFTP server",
                "Mount any SSH-accessible server. Password OR key-agent auth both work.",
                fields);
            if (values == null) return;

            string remoteName = RemoteNameBare(values["__name"]);
            List<KeyValuePair<string, string>> rcloneFields = new List<KeyValuePair<string, string>>();
            rcloneFields.Add(new KeyValuePair<string, string>("host", values["host"]));
            if (!String.IsNullOrWhiteSpace(values["port"]) && values["port"] != "22") rcloneFields.Add(new KeyValuePair<string, string>("port", values["port"]));
            rcloneFields.Add(new KeyValuePair<string, string>("user", values["user"]));
            if (!String.IsNullOrWhiteSpace(values["pass"])) rcloneFields.Add(new KeyValuePair<string, string>("pass", values["pass"]));
            CreateRemoteAndProfile("SFTP", "sftp", "S:", remoteName, "sftp", rcloneFields);
        }

        // ------- FTP (with optional explicit TLS) -------
        private void ConfigureFTPRemoteWizard()
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<ProviderField> fields = new List<ProviderField>();
            fields.Add(new ProviderField("__name", "Remote name", UniqueRemoteName("FTP"), "rclone identifier without the trailing colon.", false, true, null));
            fields.Add(new ProviderField("host", "Host", "", "FTP host (e.g. ftp.example.com).", false, true, null));
            fields.Add(new ProviderField("port", "Port", "21", "Defaults to 21 (or 990 for implicit TLS).", false, false, null));
            fields.Add(new ProviderField("user", "Username", "anonymous", "Use 'anonymous' for public servers.", false, true, null));
            fields.Add(new ProviderField("pass", "Password", "", "Leave empty for anonymous FTP.", true, false, null));
            fields.Add(new ProviderField("explicit_tls", "Use FTPS (explicit TLS)?", "false", "Enable for servers that require AUTH TLS.", false, false,
                new string[] { "false", "true" }));
            Dictionary<string, string> values = ShowProviderForm(
                "Add FTP server",
                "Plain FTP or FTPS (explicit TLS). Implicit-TLS port 990 servers should use 'true' with port 990.",
                fields);
            if (values == null) return;

            string remoteName = RemoteNameBare(values["__name"]);
            List<KeyValuePair<string, string>> rcloneFields = new List<KeyValuePair<string, string>>();
            rcloneFields.Add(new KeyValuePair<string, string>("host", values["host"]));
            if (!String.IsNullOrWhiteSpace(values["port"]) && values["port"] != "21") rcloneFields.Add(new KeyValuePair<string, string>("port", values["port"]));
            rcloneFields.Add(new KeyValuePair<string, string>("user", values["user"]));
            if (!String.IsNullOrWhiteSpace(values["pass"])) rcloneFields.Add(new KeyValuePair<string, string>("pass", values["pass"]));
            if (String.Equals(values["explicit_tls"], "true", StringComparison.OrdinalIgnoreCase)) rcloneFields.Add(new KeyValuePair<string, string>("explicit_tls", "true"));
            CreateRemoteAndProfile("FTP", "ftp", "F:", remoteName, "ftp", rcloneFields);
        }

        // ------- MEGA -------
        private void ConfigureMegaRemoteWizard()
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<ProviderField> fields = new List<ProviderField>();
            fields.Add(new ProviderField("__name", "Remote name", UniqueRemoteName("MEGA"), "rclone identifier without the trailing colon.", false, true, null));
            fields.Add(new ProviderField("user", "MEGA email", "", "Your MEGA account email.", false, true, null));
            fields.Add(new ProviderField("pass", "MEGA password", "", "Stored obfuscated by rclone.", true, true, null));
            Dictionary<string, string> values = ShowProviderForm(
                "Add MEGA",
                "Connect to a MEGA.nz account. Bandwidth/transfer quotas are visible on the MEGA web account.",
                fields);
            if (values == null) return;

            string remoteName = RemoteNameBare(values["__name"]);
            List<KeyValuePair<string, string>> rcloneFields = new List<KeyValuePair<string, string>>();
            rcloneFields.Add(new KeyValuePair<string, string>("user", values["user"]));
            rcloneFields.Add(new KeyValuePair<string, string>("pass", values["pass"]));
            CreateRemoteAndProfile("MEGA", "mega", "M:", remoteName, "mega", rcloneFields);
        }
    }
}
