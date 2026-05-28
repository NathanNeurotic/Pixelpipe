using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
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

        internal static string FormatBytes(double bytes)
        {
            if (bytes < 0) return "unknown";
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024.0 && unit < units.Length - 1) { value /= 1024.0; unit++; }
            if (unit == 0) return ((long)value).ToString() + " " + units[unit];
            return value.ToString("0.##") + " " + units[unit];
        }

        internal static string DisplayLimit(string value)
        {
            if (String.IsNullOrEmpty(value) || String.Equals(value, "off", StringComparison.OrdinalIgnoreCase)) return "Unlimited";
            return value + "/s";
        }

        internal static string NormalizeBandwidthLimit(string value)
        {
            string v = String.IsNullOrWhiteSpace(value) ? "off" : value.Trim();
            return IsValidBandwidth(v) ? v : "off";
        }

        internal static string NormalizeDriveLetter(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return DefaultDriveLetter;
            string v = value.Trim().ToUpperInvariant();
            if (v.Length == 1 && v[0] >= 'A' && v[0] <= 'Z') return v + ":";
            if (v.Length >= 2 && v[1] == ':' && v[0] >= 'A' && v[0] <= 'Z') return v.Substring(0, 2);
            return DefaultDriveLetter;
        }

        internal static string NormalizeRemoteName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return DefaultRemoteName;
            string v = value.Trim();
            return v.EndsWith(":") ? v : v + ":";
        }

        internal static string RemoteNameBare(string value)
        {
            string v = value ?? DefaultRemoteName;
            return v.EndsWith(":") ? v.Substring(0, v.Length - 1) : v;
        }

        internal static string NormalizeMountMode(string value)
        {
            return String.Equals(value, "fixed", StringComparison.OrdinalIgnoreCase) ? "fixed" : "network";
        }

        internal static string NormalizeProvider(string provider, string remote)
        {
            string p = (provider ?? "").Trim().ToLowerInvariant();
            if (p.Length == 0) p = (remote ?? "").ToLowerInvariant();
            if (p.IndexOf("pixeldrain") >= 0) return "pixeldrain";
            if (p.IndexOf("onedrive") >= 0) return "onedrive";
            if (p.IndexOf("drive") >= 0 || p.IndexOf("google") >= 0) return "drive";
            if (p.IndexOf("mega") >= 0) return "mega";
            if (p.IndexOf("dropbox") >= 0) return "dropbox";
            if (p == "box") return "box";
            if (p.IndexOf("s3") >= 0 || p.IndexOf("b2") >= 0 || p.IndexOf("r2") >= 0 || p.IndexOf("wasabi") >= 0) return "s3";
            if (p.IndexOf("webdav") >= 0 || p.IndexOf("nextcloud") >= 0) return "webdav";
            if (p.IndexOf("sftp") >= 0) return "sftp";
            if (p.IndexOf("ftp") >= 0) return "ftp";
            return p.Length == 0 ? "custom" : p;
        }

        internal static string DisplayProvider(string provider)
        {
            string p = NormalizeProvider(provider, "");
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

        private string GetDriveRoot(RemoteProfile p)
        {
            return NormalizeDriveLetter(p == null ? DefaultDriveLetter : p.DriveLetter) + "\\";
        }

        private string FirstFreePreferredDrive(string preferred)
        {
            // Try the caller's preferred letter first, then a sensible default order
            // skipping the preferred to avoid checking it twice.
            string normalizedPreferred = NormalizeDriveLetter(preferred);
            string[] fallback = new string[] { "P:", "G:", "M:", "R:", "X:", "Y:", "Z:", "W:", "S:", "O:", "K:" };
            string[] candidates = new string[fallback.Length + 1];
            candidates[0] = normalizedPreferred;
            int idx = 1;
            for (int i = 0; i < fallback.Length; i++)
            {
                string norm = NormalizeDriveLetter(fallback[i]);
                if (norm == normalizedPreferred) continue;
                candidates[idx++] = norm;
            }
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < idx; i++)
            {
                string d = candidates[i];
                bool usedByProfile = false;
                for (int j = 0; j < snapshot.Length; j++) if (String.Equals(snapshot[j].DriveLetter, d, StringComparison.OrdinalIgnoreCase)) usedByProfile = true;
                if (!usedByProfile && !DriveLetterInUse(d)) return d;
            }
            return normalizedPreferred;
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
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < snapshot.Length; i++) if (String.Equals(snapshot[i].Label, label, StringComparison.OrdinalIgnoreCase)) return true;
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

        // Parses an "N UNIT/s" string (e.g. "12.4 MB/s") into bytes/second. Returns 0
        // for unparseable or negative inputs like "unavailable" or "—".
        internal static double ParseBytesPerSec(string text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            Match m = Regex.Match(text, @"(-?\d+(?:\.\d+)?)\s*(B|KB|MB|GB|TB|PB)\s*/\s*s", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            double v;
            if (!Double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return 0;
            return v < 0 ? 0 : v * UnitMultiplier(m.Groups[2].Value);
        }

        // Parses an "N UNIT" string (e.g. "1.11 GB") into bytes. Returns 0 for
        // unparseable or negative.
        internal static long ParseBytes(string text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            Match m = Regex.Match(text, @"(-?\d+(?:\.\d+)?)\s*(B|KB|MB|GB|TB|PB)", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            double v;
            if (!Double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return 0;
            v *= UnitMultiplier(m.Groups[2].Value);
            if (v < 0) v = 0;
            if (v > Int64.MaxValue) v = Int64.MaxValue;
            return (long)v;
        }

        // Returns only the lines of `text` that contain `filter` (case-insensitive).
        // Empty filter returns the input untouched. Empty match returns a stub
        // string so the user sees what happened instead of a blank field.
        internal static string FilterLogText(string text, string filter)
        {
            if (String.IsNullOrEmpty(text)) return text ?? "";
            if (String.IsNullOrEmpty(filter)) return text;
            string[] lines = text.Replace("\r", "").Split('\n');
            StringBuilder sb = new StringBuilder();
            int kept = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                sb.AppendLine(lines[i]);
                kept++;
            }
            if (kept == 0) return "(no lines match filter '" + filter + "')";
            return sb.ToString();
        }

        // Computes used/total as 0..100 from raw bytes. Returns -1 when either
        // value is missing so callers can fall back to ParseStoragePercent or
        // skip the bar entirely.
        internal static int ComputeStoragePercent(long usedBytes, long totalBytes)
        {
            if (usedBytes < 0 || totalBytes <= 0) return -1;
            double pct = (double)usedBytes * 100.0 / (double)totalBytes;
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            return (int)Math.Round(pct);
        }

        // Parses a percentage out of a storage line like
        // "1.11 GB / 7.28 TB used (0.5%, 7.27 TB left, 30d)". Returns 0..100, clamped.
        internal static int ParseStoragePercent(string text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            Match m = Regex.Match(text, @"\((\d+(?:\.\d+)?)\s*%");
            if (!m.Success) return 0;
            double v;
            if (!Double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return 0;
            int clamped = (int)Math.Round(v);
            if (clamped < 0) clamped = 0;
            if (clamped > 100) clamped = 100;
            return clamped;
        }

        private static double UnitMultiplier(string unit)
        {
            switch ((unit ?? "").ToUpperInvariant())
            {
                case "B": return 1d;
                case "KB": return 1024d;
                case "MB": return 1024d * 1024;
                case "GB": return 1024d * 1024 * 1024;
                case "TB": return 1024d * 1024 * 1024 * 1024;
                case "PB": return 1024d * 1024 * 1024 * 1024 * 1024;
                default: return 1d;
            }
        }

        internal static string QuoteArg(string value)
        {
            if (value == null) return "\"\"";
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }
                sb.Append(c);
            }
            if (backslashes > 0) sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
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
            form.BackColor = WindowTheme.BgColor;
            form.ForeColor = WindowTheme.FgColor;
            form.Font = new Font("Segoe UI", 9.25f);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            return form;
        }

        private Button MakeDialogButton(string text, DialogResult result)
        {
            Button b = new Button();
            b.Text = text;
            b.DialogResult = result;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(84, 30);
            b.Padding = new Padding(10, 3, 10, 3);
            b.Margin = new Padding(4, 0, 0, 0);
            return b;
        }

        private string PromptForValue(string title, string message, string current)
        {
            using (Form form = MakeDialog(title, 540, 170))
            {
                TableLayoutPanel root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.ColumnCount = 1;
                root.RowCount = 3;
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Padding = new Padding(12);
                root.BackColor = form.BackColor;

                Label label = new Label();
                label.AutoSize = true;
                label.Dock = DockStyle.Fill;
                label.MaximumSize = new Size(500, 0);
                label.Text = message;
                label.ForeColor = WindowTheme.FgColor;
                label.Margin = new Padding(0, 0, 0, 10);

                TextBox textBox = new TextBox();
                textBox.Dock = DockStyle.Top;
                textBox.Text = current ?? "";
                textBox.Margin = new Padding(0, 0, 0, 14);

                FlowLayoutPanel footer = new FlowLayoutPanel();
                footer.Dock = DockStyle.Fill;
                footer.AutoSize = true;
                footer.FlowDirection = FlowDirection.RightToLeft;
                footer.WrapContents = false;
                footer.Margin = new Padding(0);

                Button cancel = MakeDialogButton("Cancel", DialogResult.Cancel);
                Button ok = MakeDialogButton("Save", DialogResult.OK);
                footer.Controls.Add(cancel);
                footer.Controls.Add(ok);

                root.Controls.Add(label, 0, 0);
                root.Controls.Add(textBox, 0, 1);
                root.Controls.Add(footer, 0, 2);
                form.Controls.Add(root);
                form.AcceptButton = ok; form.CancelButton = cancel;
                textBox.SelectAll();
                return form.ShowDialog() == DialogResult.OK ? textBox.Text : null;
            }
        }

        private string ChooseFromList(string title, string message, string[] options)
        {
            using (Form form = MakeDialog(title, 520, 380))
            {
                TableLayoutPanel root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.ColumnCount = 1;
                root.RowCount = 3;
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Padding = new Padding(12);
                root.BackColor = form.BackColor;

                Label label = new Label();
                label.AutoSize = true;
                label.Dock = DockStyle.Fill;
                label.Text = message;
                label.ForeColor = WindowTheme.FgColor;
                label.Margin = new Padding(0, 0, 0, 10);

                ListBox list = new ListBox();
                list.Dock = DockStyle.Fill;
                list.BackColor = WindowTheme.InputBg;
                list.ForeColor = WindowTheme.FgColor;
                list.Margin = new Padding(0, 0, 0, 14);
                for (int i = 0; i < options.Length; i++) list.Items.Add(options[i]);
                if (list.Items.Count > 0) list.SelectedIndex = 0;

                FlowLayoutPanel footer = new FlowLayoutPanel();
                footer.Dock = DockStyle.Fill;
                footer.AutoSize = true;
                footer.FlowDirection = FlowDirection.RightToLeft;
                footer.WrapContents = false;
                footer.Margin = new Padding(0);

                Button cancel = MakeDialogButton("Cancel", DialogResult.Cancel);
                Button ok = MakeDialogButton("Select", DialogResult.OK);
                footer.Controls.Add(cancel);
                footer.Controls.Add(ok);

                root.Controls.Add(label, 0, 0);
                root.Controls.Add(list, 0, 1);
                root.Controls.Add(footer, 0, 2);
                form.Controls.Add(root);
                form.AcceptButton = ok; form.CancelButton = cancel;
                return form.ShowDialog() == DialogResult.OK && list.SelectedItem != null ? list.SelectedItem.ToString() : null;
            }
        }
    }
}
