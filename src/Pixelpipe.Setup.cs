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
                    SaveSetting("FirstLaunchSetupDone", "1");
                }
            }
            catch (Exception ex) { LogUiIssue("first launch setup", ex); }
        }

        private void RunFirstLaunchSetup(bool manual)
        {
            try
            {
                string intro = "Pixelpipe will check rclone, WinFsp, and your rclone remotes. Continue?";
                if (!manual)
                {
                    intro += "\r\n\r\n(Click No to skip these prompts; you can re-run the wizard from Setup / dependencies later.)";
                }
                DialogResult introResult = MessageBox.Show(intro, "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (introResult != DialogResult.Yes)
                {
                    if (!manual) SaveSetting("SkipMissingDepWizard", "1");
                    return;
                }
                SaveSetting("SkipMissingDepWizard", "0");

                if (!RcloneAvailable())
                {
                    DialogResult r = MessageBox.Show("rclone was not found.\r\n\r\nYes = download portable rclone to your user profile.\r\nNo = try installing through winget instead.\r\nCancel = skip rclone setup.", "Pixelpipe setup", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) DownloadRclonePortableWithUi();
                    else if (r == DialogResult.No) InstallRcloneWithWinget();
                }

                if (!WinFspInstalled())
                {
                    DialogResult r = MessageBox.Show("WinFsp was not detected. rclone mount needs WinFsp on Windows.\r\n\r\nInstall WinFsp using winget now?", "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) InstallWinFspWithWinget();
                }

                if (RcloneAvailable() && !AnyRemoteConfigured())
                {
                    DialogResult r = MessageBox.Show("No configured Pixelpipe rclone remote was found.\r\n\r\nYes = configure Pixeldrain now.\r\nNo = open rclone config so you can add Google Drive, MEGA, OneDrive, S3, WebDAV, SFTP, or another backend.", "Pixelpipe setup", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) ConfigurePixeldrainRemoteFromPrompt(GetPrimaryProfile());
                    else if (r == DialogResult.No) OpenRcloneConfigTerminal();
                }

                if (!ApiKeyConfigured())
                {
                    DialogResult r = MessageBox.Show("Optional: save a PixelDrain API key for monthly transfer quota display?\r\n\r\nThis is stored encrypted with Windows DPAPI for your Windows user only.", "Pixelpipe quota setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) SetApiKeyFromPrompt();
                }

                setupStatusText = GetDependencyStatusLine();
                SaveProfiles();
                RebuildMenu();
                ShowBalloon("Setup check complete.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool RcloneAvailable()
        {
            try
            {
                rclonePath = FindRclonePath();
                if (File.Exists(rclonePath)) return true;
                string version = RunProcessCapture("rclone.exe", "version", 3000);
                return version.IndexOf("rclone", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private bool WinFspInstalled()
        {
            try
            {
                string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (File.Exists(Path.Combine(pf86, "WinFsp", "bin", "winfsp-x64.dll"))) return true;
                if (File.Exists(Path.Combine(pf, "WinFsp", "bin", "winfsp-x64.dll"))) return true;
                using (RegistryKey k1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinFsp")) { if (k1 != null) return true; }
                using (RegistryKey k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinFsp")) { if (k2 != null) return true; }
            }
            catch { }
            return false;
        }

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
            if (!RcloneAvailable()) return cachedRcloneRemotes;
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
                string text;
                try { text = GetDependencyStatusLine(); }
                catch (Exception ex) { LogUiIssue("dependency status", ex); text = setupStatusText; }
                BeginUi(delegate
                {
                    setupStatusText = text;
                    lastDependencyRefreshUtc = DateTime.UtcNow;
                    Interlocked.Exchange(ref dependencyRefreshingFlag, 0);
                    UpdateMenuLiveState();
                });
            });
        }

        private string GetDependencyStatusLine()
        {
            bool rclone = RcloneAvailable();
            bool winfsp = WinFspInstalled();
            bool remote = rclone && AnyRemoteConfigured();
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
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Pixelpipe/1.0";
                wc.DownloadFile(RcloneDownloadUrl, zip);
            }
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

        private string FindCurlPath()
        {
            string systemCurl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            if (File.Exists(systemCurl)) return systemCurl;
            return "curl.exe";
        }
    }
}
