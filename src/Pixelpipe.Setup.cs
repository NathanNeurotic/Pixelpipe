using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        private void FirstLaunchSetupIfNeeded()
        {
            try
            {
                bool firstRun = !String.Equals(LoadSetting("FirstLaunchSetupDone", "0"), "1", StringComparison.OrdinalIgnoreCase);
                bool skipMissingChecks = String.Equals(LoadSetting("SkipMissingDepWizard", "0"), "1", StringComparison.OrdinalIgnoreCase);
                bool missingRequired = !RcloneAvailable() || !WinFspInstalled() || !AnyRemoteConfigured();
                if (firstRun || (missingRequired && !skipMissingChecks))
                {
                    RunFirstLaunchSetup(false);
                }
            }
            catch (Exception ex) { LogUiIssue("first launch setup", ex); }
        }

        private void RunFirstLaunchSetup(bool manual)
        {
            try
            {
                ShowSetupWizard(manual);
                setupStatusText = GetDependencyStatusLine();
                SaveProfiles();
                RebuildMenu();
                if (manual) ShowBalloon("Setup check complete.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ARCH-1 step 3 (v0.15.3): cached dependency state + sync probes
        // moved into DependencyProbe. The async refresh worker stays here
        // because it composes results into the setup-status line and posts
        // back via BeginUi.
        private DependencyProbe _depProbe;
        private DependencyProbe Deps
        {
            get
            {
                if (_depProbe == null)
                {
                    _depProbe = new DependencyProbe(
                        delegate { rclonePath = FindRclonePath(); return rclonePath; },
                        LogUiIssue);
                }
                return _depProbe;
            }
        }

        private bool RcloneAvailable()
        {
            if (Deps.IsStale) RefreshDependencyStatusAsync(false);
            return Deps.RcloneAvailable;
        }

        private bool WinFspInstalled()
        {
            if (Deps.IsStale) RefreshDependencyStatusAsync(false);
            return Deps.WinFspInstalled;
        }

        // Compatibility shims used by the refresh worker; both just forward
        // to DependencyProbe.
        private bool ProbeRcloneAvailableSync()
        {
            return Deps.ProbeRcloneSync(delegate(string exe, int timeoutMs) { return RunProcessCapture(exe, "version", timeoutMs); });
        }

        private bool ProbeWinFspInstalledSync() { return Deps.ProbeWinFspSync(); }

        private bool AnyRemoteConfigured()
        {
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < snapshot.Length; i++) if (RemoteConfigured(snapshot[i])) return true;
            return false;
        }

        private bool RemoteConfigured(RemoteProfile p)
        {
            if (p == null) return false;
            string target = NormalizeRemoteName(p.Remote);
            string[] remotes = GetCachedRcloneRemotes(false);
            if (remotes == null) return false;
            for (int i = 0; i < remotes.Length; i++)
            {
                if (String.Equals(remotes[i], target, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // Returns a cached list of rclone remotes. If the last fetch is older than 30s,
        // tries to refresh. Returns the previous cache when rclone is slow or absent so
        // momentary timeouts don't make Pixelpipe falsely report remotes as missing.
        private string[] GetCachedRcloneRemotes(bool force)
        {
            if (!force && cachedRcloneRemotes != null && (DateTime.UtcNow - lastRemoteListUtc).TotalSeconds < 30)
            {
                return cachedRcloneRemotes;
            }
            // Cheap cached lookup — never triggers a fresh disk probe; if
            // the dependency cache hasn't been seeded yet this returns false
            // and we keep the existing remotes cache rather than spinning.
            if (!Deps.RcloneAvailable) return cachedRcloneRemotes;
            string output;
            try { output = RunRcloneCapture("listremotes", 6000); }
            catch (Exception ex) { LogUiIssue("listremotes", ex); return cachedRcloneRemotes; }
            if (String.IsNullOrWhiteSpace(output))
            {
                LogUiWarn("listremotes", "rclone returned empty output; keeping previous cache");
                return cachedRcloneRemotes;
            }
            System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
            string[] lines = output.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (s.EndsWith(":")) list.Add(s);
            }
            cachedRcloneRemotes = list.ToArray();
            lastRemoteListUtc = DateTime.UtcNow;
            return cachedRcloneRemotes;
        }

        private void RefreshDependencyStatusAsync(bool force)
        {
            if (Interlocked.CompareExchange(ref dependencyRefreshingFlag, 1, 0) != 0) return;
            if (!force && (DateTime.UtcNow - lastDependencyRefreshUtc).TotalSeconds < 30)
            {
                Interlocked.Exchange(ref dependencyRefreshingFlag, 0);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                // PERF-1 (v0.13.1): probe synchronously on the worker (these
                // are the slow disk/registry/process calls). UI thread reads
                // the cached fields via RcloneAvailable() / WinFspInstalled()
                // after we publish the new values via BeginUi.
                bool rcloneProbe = false, winfspProbe = false;
                try { rcloneProbe = ProbeRcloneAvailableSync(); } catch (Exception ex) { LogUiIssue("dep probe rclone", ex); }
                try { winfspProbe = ProbeWinFspInstalledSync(); } catch (Exception ex) { LogUiIssue("dep probe winfsp", ex); }
                string text;
                try { text = BuildDependencyStatusLine(rcloneProbe, winfspProbe, rcloneProbe && AnyRemoteConfigured()); }
                catch (Exception ex) { LogUiIssue("dependency status", ex); text = setupStatusText; }
                BeginUi(delegate
                {
                    Deps.PublishProbeResults(rcloneProbe, winfspProbe);
                    setupStatusText = text;
                    lastDependencyRefreshUtc = DateTime.UtcNow;
                    Interlocked.Exchange(ref dependencyRefreshingFlag, 0);
                    UpdateMenuLiveState();
                });
            });
        }

        private string GetDependencyStatusLine()
        {
            return BuildDependencyStatusLine(RcloneAvailable(), WinFspInstalled(), false);
        }

        private string BuildDependencyStatusLine(bool rclone, bool winfsp, bool remote)
        {
            if (rclone && winfsp && remote) return "Setup: ready";
            System.Collections.Generic.List<string> missing = new System.Collections.Generic.List<string>();
            if (!rclone) missing.Add("rclone");
            if (!winfsp) missing.Add("WinFsp");
            if (rclone && !remote) missing.Add("configured rclone remote");
            return "Setup: missing " + String.Join(", ", missing.ToArray());
        }

        private void DownloadRclonePortableWithUi()
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                string installed = DownloadRclonePortable();
                rclonePath = installed;
                MessageBox.Show("Portable rclone installed here:\r\n\r\n" + installed, "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                setupStatusText = GetDependencyStatusLine();
                RebuildMenu();
            }
            catch (Exception ex)
            {
                DialogResult r = MessageBox.Show("Portable rclone download failed.\r\n\r\n" + ex.Message + "\r\n\r\nTry winget installation instead?", "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) InstallRcloneWithWinget();
            }
            finally { Cursor.Current = Cursors.Default; }
        }

        private string DownloadRclonePortable()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apps", "rclone");
            Directory.CreateDirectory(installDir);
            string tempRoot = Path.Combine(Path.GetTempPath(), "Pixelpipe-rclone-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            string zip = Path.Combine(tempRoot, "rclone.zip");
            string sumsPath = Path.Combine(tempRoot, "SHA256SUMS");
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Pixelpipe/1.0";
                wc.DownloadFile(RcloneDownloadUrl, zip);
                wc.DownloadFile(RcloneSha256SumsUrl, sumsPath);
            }
            // SEC-3 (v0.13.0): refuse to extract/run a tampered or
            // truncated download. Compute SHA-256 of the zip, parse the
            // published SHA256SUMS for the entry whose filename matches
            // our pinned zip, compare. Any mismatch deletes everything
            // we just downloaded and throws.
            string actual = ComputeSha256Hex(zip);
            string expected = ParseSha256ForFile(File.ReadAllText(sumsPath, Encoding.UTF8), RcloneZipName);
            if (String.IsNullOrEmpty(expected))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
                throw new InvalidOperationException("Pixelpipe could not find a SHA256 entry for " + RcloneZipName + " in the published SHA256SUMS file. Refusing to install an unverified rclone.");
            }
            if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
                throw new InvalidOperationException("rclone download checksum mismatch.\r\nExpected: " + expected + "\r\nGot:      " + actual + "\r\n\r\nPixelpipe refused to extract this download. Network is compromised or the rclone CDN was tampered with.");
            }
            LogUiInfo("rclone download", "verified SHA-256 " + actual + " for " + RcloneZipName);
            string extract = Path.Combine(tempRoot, "extract");
            Directory.CreateDirectory(extract);
            ZipFile.ExtractToDirectory(zip, extract);
            string[] matches = Directory.GetFiles(extract, "rclone.exe", SearchOption.AllDirectories);
            if (matches.Length == 0) throw new FileNotFoundException("Downloaded rclone zip did not contain rclone.exe");
            string dest = Path.Combine(installDir, "rclone.exe");
            File.Copy(matches[0], dest, true);
            try { Directory.Delete(tempRoot, true); } catch { }
            return dest;
        }

        internal static string ComputeSha256Hex(string filePath)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            using (FileStream fs = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
            }
        }

        // Pure helper: rclone's SHA256SUMS is one entry per line, formatted
        // as "<hex>  <filename>". Returns the hex hash whose filename matches
        // `wanted` (case-insensitive), or empty string if not found.
        internal static string ParseSha256ForFile(string sumsContent, string wanted)
        {
            if (String.IsNullOrEmpty(sumsContent) || String.IsNullOrEmpty(wanted)) return "";
            string[] lines = sumsContent.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                int sp = line.IndexOf(' ');
                if (sp <= 0) continue;
                string hash = line.Substring(0, sp).Trim();
                string rest = line.Substring(sp).Trim().TrimStart('*');
                if (String.Equals(rest, wanted, StringComparison.OrdinalIgnoreCase)) return hash;
            }
            return "";
        }

        private void InstallRcloneWithWinget()
        {
            if (!WingetAvailable())
            {
                MessageBox.Show("winget was not found. Windows installs winget through Microsoft App Installer. The help page will open now.", "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OpenWingetInstallHelp();
                return;
            }
            RunInstallerTerminal("winget install -e --id " + WingetRcloneId + " --accept-package-agreements --accept-source-agreements", false);
        }

        private void InstallWinFspWithWinget()
        {
            if (!WingetAvailable())
            {
                MessageBox.Show("winget was not found. Windows installs winget through Microsoft App Installer. The help page will open now.", "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OpenWingetInstallHelp();
                return;
            }
            RunInstallerTerminal("winget install -e --id " + WingetWinFspId + " --accept-package-agreements --accept-source-agreements", true);
        }

        private bool WingetAvailable()
        {
            try
            {
                string result = RunProcessCapture("winget.exe", "--version", 3500);
                return result.IndexOf("v", StringComparison.OrdinalIgnoreCase) >= 0 || result.Length > 0;
            }
            catch { return false; }
        }

        private void RunInstallerTerminal(string command, bool elevated)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoExit -ExecutionPolicy Bypass -Command " + QuoteArg("Write-Host 'Pixelpipe setup'; " + command + "; Write-Host ''; Read-Host 'Press Enter to close this setup window'");
                psi.UseShellExecute = true;
                if (elevated) psi.Verb = "runas";
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenWingetInstallHelp()
        {
            try
            {
                Process.Start("https://learn.microsoft.com/en-us/windows/package-manager/winget/");
                Process.Start("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1");
            }
            catch
            {
                try { Process.Start("https://apps.microsoft.com/detail/9nblggh4nns1"); } catch { }
            }
        }

        private void OpenRcloneConfigTerminal()
        {
            try
            {
                if (!RcloneAvailable())
                {
                    MessageBox.Show("rclone is not available yet.", "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoExit -Command " + QuoteArg("& " + QuoteArg(rclonePath) + " config");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string FindRclonePath()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] candidates = new string[]
            {
                Path.Combine(profile, "Apps", "rclone", "rclone.exe"),
                Path.Combine(pf, "rclone", "rclone.exe"),
                @"C:\rclone\rclone.exe"
            };
            for (int i = 0; i < candidates.Length; i++) if (File.Exists(candidates[i])) return candidates[i];

            // rclone's official Windows installer drops into a versioned folder like
            // C:\Program Files\rclone-v1.71.1-windows-amd64\. Glob it so we don't have
            // to chase the latest version in code.
            try
            {
                if (Directory.Exists(pf))
                {
                    string[] dirs = Directory.GetDirectories(pf, "rclone-v*-windows-*");
                    Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                    for (int i = dirs.Length - 1; i >= 0; i--)
                    {
                        string candidate = Path.Combine(dirs[i], "rclone.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch { }

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] pathDirs = path.Split(Path.PathSeparator);
            for (int i = 0; i < pathDirs.Length; i++)
            {
                try
                {
                    string full = Path.Combine(pathDirs[i].Trim(), "rclone.exe");
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return "rclone.exe";
        }
    }
}
