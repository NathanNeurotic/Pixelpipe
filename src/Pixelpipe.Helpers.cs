using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        private object GetDictValue(Dictionary<string, object> dict, string key)
        {
            if (dict == null || key == null) return null;
            object value;
            return dict.TryGetValue(key, out value) ? value : null;
        }

        private long ToLong(object value)
        {
            if (value == null) return -1;
            try
            {
                if (value is int) return (int)value;
                if (value is long) return (long)value;
                if (value is double) return (long)(double)value;
                if (value is decimal) return (long)(decimal)value;
                long parsed;
                if (Int64.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out parsed)) return parsed;
            }
            catch { }
            return -1;
        }

        private string RunProcessCapture(string fileName, string arguments, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process p = Process.Start(psi);
                if (p == null) return "";
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return ""; }
                return p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            }
            catch { return ""; }
        }

        private string RunRcloneCapture(string arguments, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = rclonePath;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process p = Process.Start(psi);
                if (p == null) return "";
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return ""; }
                return p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            }
            catch (Exception ex) { return ex.Message; }
        }

        private long ExtractLong(string text, string key)
        {
            try
            {
                Match m = Regex.Match(text, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(-?[0-9]+)");
                if (!m.Success) return -1;
                long value;
                if (Int64.TryParse(m.Groups[1].Value, out value)) return value;
            }
            catch { }
            return -1;
        }

        private double ExtractDouble(string text, string key)
        {
            try
            {
                Match m = Regex.Match(text, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)");
                if (!m.Success) return 0.0;
                double value;
                if (Double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) return value;
            }
            catch { }
            return 0.0;
        }

        internal static string FormatBytesValue(double bytes)
        {
            if (bytes < 0) return "unknown";
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024.0 && unit < units.Length - 1) { value /= 1024.0; unit++; }
            if (unit == 0) return ((long)value).ToString() + " " + units[unit];
            return value.ToString("0.##") + " " + units[unit];
        }

        private string FormatBytes(double bytes)
        {
            return FormatBytesValue(bytes);
        }

        internal static string DisplayLimitValue(string value)
        {
            if (String.IsNullOrEmpty(value) || String.Equals(value, "off", StringComparison.OrdinalIgnoreCase)) return "Unlimited";
            return value + "/s";
        }

        private string DisplayLimit(string value)
        {
            return DisplayLimitValue(value);
        }

        internal static string NormalizeDriveLetterValue(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return DefaultDriveLetter;
            string v = value.Trim().ToUpperInvariant();
            if (v.Length == 1 && v[0] >= 'A' && v[0] <= 'Z') return v + ":";
            if (v.Length >= 2 && v[1] == ':' && v[0] >= 'A' && v[0] <= 'Z') return v.Substring(0, 2);
            return DefaultDriveLetter;
        }

        private string NormalizeDriveLetter(string value)
        {
            return NormalizeDriveLetterValue(value);
        }

        internal static string NormalizeRemoteNameValue(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return DefaultRemoteName;
            string v = value.Trim();
            return v.EndsWith(":") ? v : v + ":";
        }

        private string NormalizeRemoteName(string value)
        {
            return NormalizeRemoteNameValue(value);
        }

        internal static string RemoteNameBareValue(string value)
        {
            string v = value ?? DefaultRemoteName;
            return v.EndsWith(":") ? v.Substring(0, v.Length - 1) : v;
        }

        private string RemoteNameBare(string value)
        {
            return RemoteNameBareValue(value);
        }

        internal static string NormalizeMountModeValue(string value)
        {
            return String.Equals(value, "fixed", StringComparison.OrdinalIgnoreCase) ? "fixed" : "network";
        }

        private string NormalizeMountMode(string value)
        {
            return NormalizeMountModeValue(value);
        }

        internal static string NormalizeProviderValue(string provider, string remote)
        {
            string p = (provider ?? "").Trim().ToLowerInvariant();
            if (p.Length == 0) p = (remote ?? "").ToLowerInvariant();
            if (p.IndexOf("pixeldrain") >= 0) return "pixeldrain";
            if (p.IndexOf("drive") >= 0 || p.IndexOf("google") >= 0) return "drive";
            if (p.IndexOf("mega") >= 0) return "mega";
            if (p.IndexOf("onedrive") >= 0) return "onedrive";
            if (p.IndexOf("dropbox") >= 0) return "dropbox";
            if (p == "box") return "box";
            if (p.IndexOf("s3") >= 0 || p.IndexOf("b2") >= 0 || p.IndexOf("r2") >= 0 || p.IndexOf("wasabi") >= 0) return "s3";
            if (p.IndexOf("webdav") >= 0 || p.IndexOf("nextcloud") >= 0) return "webdav";
            if (p.IndexOf("sftp") >= 0) return "sftp";
            if (p.IndexOf("ftp") >= 0) return "ftp";
            return p.Length == 0 ? "custom" : p;
        }

        private string NormalizeProvider(string provider, string remote)
        {
            return NormalizeProviderValue(provider, remote);
        }

        internal static string DisplayProviderValue(string provider)
        {
            string p = NormalizeProviderValue(provider, "");
            if (p == "pixeldrain") return "Pixeldrain";
            if (p == "drive") return "Google Drive";
            if (p == "mega") return "MEGA";
            if (p == "onedrive") return "OneDrive";
            if (p == "dropbox") return "Dropbox";
            if (p == "box") return "Box";
            if (p == "s3") return "S3-compatible";
            if (p == "webdav") return "WebDAV";
            if (p == "sftp") return "SFTP";
            if (p == "ftp") return "FTP";
            return "Custom";
        }

        private string DisplayProvider(string provider)
        {
            return DisplayProviderValue(provider);
        }

        private string GetDriveRoot(RemoteProfile p)
        {
            return NormalizeDriveLetter(p == null ? DefaultDriveLetter : p.DriveLetter) + "\\";
        }

        private string FirstFreePreferredDrive(string preferred)
        {
            string[] order = new string[] { preferred, "P:", "G:", "M:", "R:", "X:", "Y:", "Z:", "W:", "S:", "O:", "B:" };
            for (int i = 0; i < order.Length; i++)
            {
                string d = NormalizeDriveLetter(order[i]);
                bool usedByProfile = false;
                for (int j = 0; j < profiles.Count; j++) if (String.Equals(profiles[j].DriveLetter, d, StringComparison.OrdinalIgnoreCase)) usedByProfile = true;
                if (!usedByProfile && !DriveLetterInUse(d)) return d;
            }
            return NormalizeDriveLetter(preferred);
        }

        private string UniqueLabel(string baseLabel)
        {
            int n = 1;
            string candidate = baseLabel;
            while (ProfileLabelExists(candidate))
            {
                n++;
                candidate = baseLabel + " " + n.ToString();
            }
            return candidate;
        }

        private bool ProfileLabelExists(string label)
        {
            for (int i = 0; i < profiles.Count; i++) if (String.Equals(profiles[i].Label, label, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private string UniqueRemoteName(string baseLabel)
        {
            string clean = Regex.Replace(baseLabel, "[^A-Za-z0-9_-]+", "");
            if (clean.Length == 0) clean = "Remote";
            int n = 1;
            string candidate = clean;
            while (HasProfileForRemote(candidate + ":"))
            {
                n++;
                candidate = clean + n.ToString();
            }
            return candidate;
        }

        internal static string ToStringValue(object value, string fallback)
        {
            if (value == null) return fallback;
            string s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            return String.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        internal static bool ToBool(object value)
        {
            if (value == null) return false;
            if (value is bool) return (bool)value;
            string s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            return String.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || String.Equals(s, "1", StringComparison.OrdinalIgnoreCase) || String.Equals(s, "yes", StringComparison.OrdinalIgnoreCase);
        }

        internal static string SafeFileName(string value)
        {
            string s = String.IsNullOrWhiteSpace(value) ? "remote" : value;
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        internal static string TrimForMenu(string value, int max)
        {
            if (String.IsNullOrEmpty(value) || value.Length <= max) return value;
            return value.Substring(0, max) + "...";
        }

        private string Quote(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        private string QuoteArg(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private bool HasArg(string[] args, string wanted)
        {
            return Program.HasArg(args, wanted);
        }

        private Form MakeDialog(string title, int width, int height)
        {
            Form form = new Form();
            form.Text = title;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.Width = width;
            form.Height = height;
            form.BackColor = Color.FromArgb(18, 22, 28);
            form.ForeColor = Color.WhiteSmoke;
            return form;
        }

        private string PromptForValue(string title, string message, string current)
        {
            using (Form form = MakeDialog(title, 540, 170))
            using (Label label = new Label())
            using (TextBox textBox = new TextBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                label.Left = 12; label.Top = 12; label.Width = 500; label.Height = 36; label.Text = message; label.ForeColor = Color.WhiteSmoke;
                textBox.Left = 12; textBox.Top = 54; textBox.Width = 500; textBox.Text = current ?? ""; textBox.SelectAll();
                ok.Text = "Save"; ok.Left = 336; ok.Top = 88; ok.Width = 84; ok.DialogResult = DialogResult.OK;
                cancel.Text = "Cancel"; cancel.Left = 428; cancel.Top = 88; cancel.Width = 84; cancel.DialogResult = DialogResult.Cancel;
                form.Controls.Add(label); form.Controls.Add(textBox); form.Controls.Add(ok); form.Controls.Add(cancel);
                form.AcceptButton = ok; form.CancelButton = cancel;
                return form.ShowDialog() == DialogResult.OK ? textBox.Text : null;
            }
        }

        private string ChooseFromList(string title, string message, string[] options)
        {
            using (Form form = MakeDialog(title, 520, 380))
            using (Label label = new Label())
            using (ListBox list = new ListBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                label.Left = 12; label.Top = 12; label.Width = 480; label.Height = 30; label.Text = message; label.ForeColor = Color.WhiteSmoke;
                list.Left = 12; list.Top = 48; list.Width = 480; list.Height = 240; list.BackColor = Color.FromArgb(14, 18, 24); list.ForeColor = Color.WhiteSmoke;
                for (int i = 0; i < options.Length; i++) list.Items.Add(options[i]);
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                ok.Text = "Select"; ok.Left = 316; ok.Top = 302; ok.Width = 84; ok.DialogResult = DialogResult.OK;
                cancel.Text = "Cancel"; cancel.Left = 408; cancel.Top = 302; cancel.Width = 84; cancel.DialogResult = DialogResult.Cancel;
                form.Controls.Add(label); form.Controls.Add(list); form.Controls.Add(ok); form.Controls.Add(cancel);
                form.AcceptButton = ok; form.CancelButton = cancel;
                return form.ShowDialog() == DialogResult.OK && list.SelectedItem != null ? list.SelectedItem.ToString() : null;
            }
        }
    }
}
