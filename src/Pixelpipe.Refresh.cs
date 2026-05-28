using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        private void QueueRefresh(bool forceAbout, bool showErrors)
        {
            if (Interlocked.CompareExchange(ref refreshingFlag, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    RemoteProfile[] snapshot = SnapshotProfiles();
                    for (int i = 0; i < snapshot.Length; i++) RefreshProfile(snapshot[i], forceAbout);

                    bool refreshQuota = forceAbout || (DateTime.UtcNow - lastQuotaRefreshUtc).TotalSeconds > 120;
                    if (refreshQuota)
                    {
                        transferQuotaText = RefreshPixeldrainTransferQuota();
                        lastQuotaRefreshUtc = DateTime.UtcNow;
                    }
                }
                catch (Exception ex) { LogUiIssue("queue refresh", ex); }
                finally
                {
                    BeginUi(delegate
                    {
                        Interlocked.Exchange(ref refreshingFlag, 0);
                        UpdateMenuLiveState();
                        UpdateMainWindowLiveState();
                        UpdateQuickControlLiveState();
                    });
                }
            });
        }

        private void RefreshProfile(RemoteProfile p, bool forceAbout)
        {
            if (p == null) return;
            bool mounted = IsMounted(p);
            if (mounted)
            {
                string stats = RunRcloneCapture("rc core/stats --rc-addr 127.0.0.1:" + p.RcPort.ToString() + " --rc-no-auth", 3500);
                if (!String.IsNullOrEmpty(stats))
                {
                    long bytes = ExtractLong(stats, "bytes");
                    double speed = ExtractDouble(stats, "speed");
                    long transferringCount = ExtractLong(stats, "transferring");
                    p.SessionText = FormatBytes(bytes);
                    p.SpeedText = FormatBytes(speed) + "/s";
                    DetectTransferCompletion(p, bytes, transferringCount);
                }
                else
                {
                    p.SessionText = "unavailable";
                    p.SpeedText = "unavailable";
                }
                p.StatusText = "mounted on " + GetDriveRoot(p) + (p.FullCache ? " - full cache" : " - low overhead");
            }
            else
            {
                if (p.TransferActive)
                {
                    // Mount went away mid-transfer; clear the latch so a later
                    // remount doesn't immediately report a fake delta.
                    p.TransferActive = false;
                    p.TransferStartBytes = 0;
                }
                p.StatusText = "not mounted";
                p.SessionText = "not mounted";
                p.SpeedText = "not mounted";
            }

            bool refreshAbout = forceAbout || (DateTime.UtcNow - p.LastAboutRefreshUtc).TotalSeconds > 120;
            if (refreshAbout)
            {
                string about = RunRcloneCapture("about " + QuoteArg(NormalizeRemoteName(p.Remote)) + " --json", 8000);
                if (!String.IsNullOrEmpty(about))
                {
                    long used = ExtractLong(about, "used");
                    long total = ExtractLong(about, "total");
                    long free = ExtractLong(about, "free");
                    if (used >= 0 && total > 0) p.StorageText = FormatBytes(used) + " used / " + FormatBytes(total) + " total";
                    else if (used >= 0 && free >= 0) p.StorageText = FormatBytes(used) + " used / " + FormatBytes(free) + " free";
                    else if (about.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || about.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0) p.StorageText = "unavailable - check rclone config/API access";
                    else p.StorageText = "not reported by backend";
                }
                else p.StorageText = "unavailable";
                p.LastAboutRefreshUtc = DateTime.UtcNow;
            }
        }

        // Detects rclone transfer-batch completion by latching when transferring
        // goes from 0 to >0, then firing a balloon when it returns to 0. The
        // 10 MB floor stops trivial directory listings or VFS background syncs
        // from being announced as user-meaningful transfers.
        private const long TransferNotificationMinBytes = 10L * 1024 * 1024;

        private void DetectTransferCompletion(RemoteProfile p, long bytesNow, long transferringCount)
        {
            if (!String.Equals(LoadSetting("TransferNotificationsEnabled", "1"), "1", StringComparison.OrdinalIgnoreCase))
            {
                p.TransferActive = false;
                p.TransferStartBytes = 0;
                return;
            }
            if (transferringCount > 0)
            {
                if (!p.TransferActive)
                {
                    p.TransferActive = true;
                    p.TransferStartBytes = bytesNow >= 0 ? bytesNow : 0;
                }
                return;
            }
            if (!p.TransferActive) return;
            long delta = bytesNow - p.TransferStartBytes;
            p.TransferActive = false;
            p.TransferStartBytes = 0;
            if (delta >= TransferNotificationMinBytes)
            {
                BeginUi(delegate
                {
                    ShowBalloon(p.Label + ": transfer finished — " + FormatBytes(delta) + " moved");
                });
            }
        }

        private bool ApiKeyConfigured()
        {
            return !String.IsNullOrWhiteSpace(LoadApiKey());
        }

        private void OpenApiKeysPage()
        {
            try { Process.Start("https://pixeldrain.com/user/api_keys"); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SetApiKeyFromPrompt()
        {
            string existing = LoadApiKey();
            string value = PromptForApiKey(existing);
            if (value == null) return;
            value = value.Trim();
            if (value.Length == 0)
            {
                ClearApiKey();
                return;
            }
            SaveApiKey(value);
            transferQuotaText = "Transfer quota: checking...";
            QueueRefresh(true, true);
            ShowBalloon("PixelDrain API key saved for quota checks.");
        }

        private string PromptForApiKey(string existing)
        {
            using (Form form = MakeDialog("PixelDrain API key", 540, 180))
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
                label.Text = "Paste a PixelDrain API key. It is stored encrypted for your Windows user only.";
                label.ForeColor = WindowTheme.FgColor;
                label.Margin = new Padding(0, 0, 0, 10);

                TextBox textBox = new TextBox();
                textBox.Dock = DockStyle.Top;
                textBox.UseSystemPasswordChar = true;
                textBox.Text = existing ?? "";
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

        private void ClearApiKey()
        {
            try { DeleteSetting("PixeldrainApiKeyProtected"); }
            catch (Exception ex) { LogUiIssue("clear api key", ex); }
            transferQuotaText = "Transfer quota: PixelDrain API key not set";
            RebuildMenu();
            ShowBalloon("PixelDrain API key cleared.");
        }

        private void SaveApiKey(string apiKey)
        {
            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(apiKey ?? "");
                byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                SaveSetting("PixeldrainApiKeyProtected", Convert.ToBase64String(protectedBytes));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save API key with Windows DPAPI.\r\n\r\n" + ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string LoadApiKey()
        {
            string keyValue = LoadSettingRaw("PixeldrainApiKeyProtected");
            if (String.IsNullOrWhiteSpace(keyValue)) return "";
            try
            {
                byte[] protectedBytes = Convert.FromBase64String(keyValue);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch { return ""; }
        }

        private string RefreshPixeldrainTransferQuota()
        {
            bool hasPixeldrain = false;
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < snapshot.Length; i++) if (String.Equals(snapshot[i].Provider, "pixeldrain", StringComparison.OrdinalIgnoreCase)) hasPixeldrain = true;
            if (!hasPixeldrain) return "Transfer quota: PixelDrain profile not configured";
            string apiKey = LoadApiKey();
            if (String.IsNullOrWhiteSpace(apiKey)) return "Transfer quota: PixelDrain API key not set";
            try
            {
                string userJson = HttpGetPixeldrain("https://pixeldrain.com/api/user", apiKey, 7000);
                if (String.IsNullOrWhiteSpace(userJson)) return "Transfer quota: unavailable";
                if (userJson.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0) return "Transfer quota: API key rejected";
                JavaScriptSerializer js = new JavaScriptSerializer();
                object parsed = js.DeserializeObject(userJson);
                Dictionary<string, object> user = parsed as Dictionary<string, object>;
                if (user == null) return "Transfer quota: unexpected API response";
                long used = ToLong(GetDictValue(user, "monthly_transfer_used"));
                long cap = ToLong(GetDictValue(user, "monthly_transfer_cap"));
                Dictionary<string, object> sub = GetDictValue(user, "subscription") as Dictionary<string, object>;
                long subCap = ToLong(sub == null ? null : GetDictValue(sub, "monthly_transfer_cap"));
                if (cap <= 0 && subCap > 0) cap = subCap;
                long timeSeriesUsed = GetTransferPaidLast30Days(apiKey);
                if (timeSeriesUsed >= 0) used = timeSeriesUsed;
                if (cap > 0)
                {
                    double pct = (double)used * 100.0 / (double)cap;
                    long remaining = cap - used;
                    if (remaining < 0) remaining = 0;
                    return "Transfer quota: " + FormatBytes(used) + " / " + FormatBytes(cap) + " used (" + pct.ToString("0.#") + "%, " + FormatBytes(remaining) + " left, 30d)";
                }
                return "Transfer quota: " + FormatBytes(used) + " used in last 30d / no fixed cap";
            }
            catch (WebException ex)
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.Unauthorized) return "Transfer quota: API key rejected";
                return "Transfer quota: unavailable - " + ex.Message;
            }
            catch (Exception ex)
            {
                return "Transfer quota: unavailable - " + ex.Message;
            }
        }

        private long GetTransferPaidLast30Days(string apiKey)
        {
            try
            {
                DateTime end = DateTime.UtcNow;
                DateTime start = end.AddDays(-30);
                string url = "https://pixeldrain.com/api/user/time_series/transfer_paid?start=" + Uri.EscapeDataString(start.ToString("yyyy-MM-ddTHH:mm:ssZ")) + "&end=" + Uri.EscapeDataString(end.ToString("yyyy-MM-ddTHH:mm:ssZ")) + "&interval=60";
                string json = HttpGetPixeldrain(url, apiKey, 7000);
                if (String.IsNullOrWhiteSpace(json)) return -1;
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> root = js.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return -1;
                object amountsObj = GetDictValue(root, "amounts");
                object[] amounts = amountsObj as object[];
                if (amounts == null) return -1;
                long sum = 0;
                for (int i = 0; i < amounts.Length; i++) sum += ToLong(amounts[i]);
                return sum;
            }
            catch { return -1; }
        }

        private string HttpGetPixeldrain(string url, string apiKey, int timeoutMs)
        {
            // TLS protocol and Expect100Continue are configured once in Program.ConfigureModernTls.
            string token = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + apiKey));
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "Pixelpipe/1.0";
                req.Headers[HttpRequestHeader.Authorization] = "Basic " + token;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream stream = resp.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                if (ex.Response != null) throw;
                string fallback = CurlGetPixeldrain(url, token, timeoutMs);
                if (!String.IsNullOrWhiteSpace(fallback)) return fallback;
                throw;
            }
            catch
            {
                string fallback = CurlGetPixeldrain(url, token, timeoutMs);
                if (!String.IsNullOrWhiteSpace(fallback)) return fallback;
                throw;
            }
        }

        private string CurlGetPixeldrain(string url, string basicToken, int timeoutMs)
        {
            try
            {
                string curl = FindCurlPath();
                if (String.IsNullOrWhiteSpace(curl)) return "";
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = curl;
                psi.Arguments = "-fsSL --connect-timeout 8 --max-time " + Math.Max(10, timeoutMs / 1000).ToString() + " -H " + QuoteArg("Authorization: Basic " + basicToken) + " -H " + QuoteArg("User-Agent: Pixelpipe/1.0 curl-fallback") + " " + QuoteArg(url);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process p = Process.Start(psi);
                if (p == null) return "";
                if (!p.WaitForExit(timeoutMs + 3000)) { try { p.Kill(); } catch { } return ""; }
                string stdout = p.StandardOutput.ReadToEnd();
                if (p.ExitCode == 0) return stdout;
                return "";
            }
            catch { return ""; }
        }
    }
}
