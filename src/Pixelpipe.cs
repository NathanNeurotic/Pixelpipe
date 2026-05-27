using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Net;
using System.Web.Script.Serialization;

namespace Pixelpipe
{
    internal static class Program
    {
        private static void ConfigureModernTls()
        {
            try
            {
                // Older .NET Framework builds can default to TLS 1.0/1.1.
                // PixelDrain currently requires a modern TLS handshake.
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 |
                    (SecurityProtocolType)12288; // TLS 1.3 where supported by the OS/runtime.
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.DefaultConnectionLimit = 8;
            }
            catch { }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            ConfigureModernTls();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayContext(args));
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private const string RemoteName = "Pixeldrain:";
        private const string DriveLetter = "P:";
        private const string DriveRoot = "P:\\";
        private const string RcAddress = "127.0.0.1:55729";
        private const string AppName = "Pixelpipe";
        private const string LegacyAppName = "PixeldrainAioMountTray";
        private const string RcloneDownloadUrl = "https://downloads.rclone.org/rclone-current-windows-amd64.zip";
        private const string WingetRcloneId = "Rclone.Rclone";
        private const string WingetWinFspId = "WinFsp.WinFsp";

        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem statusItem;
        private readonly ToolStripMenuItem storageItem;
        private readonly ToolStripMenuItem transferQuotaItem;
        private readonly ToolStripMenuItem sessionItem;
        private readonly ToolStripMenuItem speedItem;
        private readonly ToolStripMenuItem limitItem;
        private readonly ToolStripMenuItem mountLowItem;
        private readonly ToolStripMenuItem mountFullItem;
        private readonly ToolStripMenuItem unmountItem;
        private readonly ToolStripMenuItem openDriveItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem adminWarningItem;
        private readonly ToolStripMenuItem logItem;
        private readonly ToolStripMenuItem refreshItem;
        private readonly ToolStripMenuItem diagnosticsItem;
        private readonly ToolStripMenuItem exitItem;
        private readonly ToolStripMenuItem bandwidthMenu;
        private readonly ToolStripMenuItem apiKeyMenu;
        private readonly ToolStripMenuItem apiKeyStatusItem;
        private readonly ToolStripMenuItem setApiKeyItem;
        private readonly ToolStripMenuItem clearApiKeyItem;
        private readonly ToolStripMenuItem openApiKeysItem;
        private readonly ToolStripMenuItem setupMenu;
        private readonly ToolStripMenuItem setupStatusItem;
        private readonly ToolStripMenuItem runSetupItem;
        private readonly ToolStripMenuItem downloadRclonePortableItem;
        private readonly ToolStripMenuItem installRcloneWingetItem;
        private readonly ToolStripMenuItem installWinFspWingetItem;
        private readonly ToolStripMenuItem configureRemoteItem;
        private readonly ToolStripMenuItem openRcloneConfigItem;
        private readonly ToolStripMenuItem openWingetHelpItem;
        private readonly List<ToolStripMenuItem> bandwidthChoices;
        private readonly System.Windows.Forms.Timer timer;

        private Process mountProcess;
        private bool mountUsesFullCache;
        private bool refreshing;
        private DateTime lastAboutRefreshUtc = DateTime.MinValue;
        private DateTime lastAccountRefreshUtc = DateTime.MinValue;
        private string selectedBandwidth;
        private string rclonePath;
        private string logDir;
        private string logFile;
        private string statusText = "Status: not mounted";
        private string storageText = "Storage: not checked";
        private string transferQuotaText = "Transfer quota: API key not set";
        private string sessionText = "Session traffic: not checked";
        private string speedText = "Current speed: not checked";
        private string setupStatusText = "Setup: not checked";
        private DateTime lastDependencyRefreshUtc = DateTime.MinValue;
        private bool dependencyRefreshing;

        public TrayContext(string[] args)
        {
            logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
            logFile = Path.Combine(logDir, "rclone-mount.log");
            Directory.CreateDirectory(logDir);

            selectedBandwidth = LoadSetting("BandwidthLimit", "off");
            rclonePath = FindRclonePath();

            menu = new ContextMenuStrip();
            menu.Opening += delegate { UpdateMenuText(); QueueRefresh(false, false); RefreshDependencyStatusAsync(false); };

            statusItem = DisabledItem(statusText);
            storageItem = DisabledItem(storageText);
            transferQuotaItem = DisabledItem(transferQuotaText);
            sessionItem = DisabledItem(sessionText);
            speedItem = DisabledItem(speedText);
            limitItem = DisabledItem("Bandwidth limit: " + DisplayLimit(selectedBandwidth));
            adminWarningItem = DisabledItem("Warning: running as Administrator; P: may be hidden from normal Explorer");

            mountLowItem = new ToolStripMenuItem("Mount Pixelpipe - low overhead", null, delegate { Mount(false); });
            mountFullItem = new ToolStripMenuItem("Mount Pixelpipe - full cache", null, delegate { Mount(true); });
            unmountItem = new ToolStripMenuItem("Unmount Pixelpipe", null, delegate { Unmount(); });
            openDriveItem = new ToolStripMenuItem("Open P:\\", null, delegate { OpenDrive(); });
            refreshItem = new ToolStripMenuItem("Refresh usage now", null, delegate { QueueRefresh(true, true); });
            logItem = new ToolStripMenuItem("Open rclone log", null, delegate { OpenLog(); });
            diagnosticsItem = new ToolStripMenuItem("Copy diagnostics", null, delegate { CopyDiagnostics(); });
            startupItem = new ToolStripMenuItem("Auto-mount at Windows startup", null, delegate { ToggleStartup(); });
            exitItem = new ToolStripMenuItem("Exit", null, delegate { ExitApp(); });

            bandwidthChoices = new List<ToolStripMenuItem>();
            bandwidthMenu = new ToolStripMenuItem("Set bandwidth limit");
            AddBandwidthChoice("off", "Unlimited");
            AddBandwidthChoice("1M", "1 MB/s");
            AddBandwidthChoice("5M", "5 MB/s");
            AddBandwidthChoice("10M", "10 MB/s");
            AddBandwidthChoice("25M", "25 MB/s");
            AddBandwidthChoice("50M", "50 MB/s");
            AddBandwidthChoice("100M", "100 MB/s");
            AddBandwidthChoice("250M", "250 MB/s");

            apiKeyMenu = new ToolStripMenuItem("PixelDrain API key");
            apiKeyStatusItem = DisabledItem(ApiKeyConfigured() ? "API key: configured" : "API key: not set");
            setApiKeyItem = new ToolStripMenuItem("Set / update API key...", null, delegate { SetApiKeyFromPrompt(); });
            clearApiKeyItem = new ToolStripMenuItem("Clear stored API key", null, delegate { ClearApiKey(); });
            openApiKeysItem = new ToolStripMenuItem("Open PixelDrain API keys page", null, delegate { OpenApiKeysPage(); });
            apiKeyMenu.DropDownItems.Add(apiKeyStatusItem);
            apiKeyMenu.DropDownItems.Add(setApiKeyItem);
            apiKeyMenu.DropDownItems.Add(clearApiKeyItem);
            apiKeyMenu.DropDownItems.Add(openApiKeysItem);

            setupMenu = new ToolStripMenuItem("Setup / dependencies");
            setupStatusItem = DisabledItem("Setup: checking...");
            runSetupItem = new ToolStripMenuItem("Run first-time setup wizard", null, delegate { RunFirstLaunchSetup(true); });
            downloadRclonePortableItem = new ToolStripMenuItem("Download portable rclone now", null, delegate { DownloadRclonePortableWithUi(); });
            installRcloneWingetItem = new ToolStripMenuItem("Install/update rclone with winget", null, delegate { InstallRcloneWithWinget(); });
            installWinFspWingetItem = new ToolStripMenuItem("Install WinFsp with winget", null, delegate { InstallWinFspWithWinget(); });
            configureRemoteItem = new ToolStripMenuItem("Configure PixelDrain rclone remote", null, delegate { ConfigurePixeldrainRemoteFromPrompt(); });
            openRcloneConfigItem = new ToolStripMenuItem("Open rclone config in terminal", null, delegate { OpenRcloneConfigTerminal(); });
            openWingetHelpItem = new ToolStripMenuItem("Open winget/App Installer help", null, delegate { OpenWingetInstallHelp(); });
            setupMenu.DropDownItems.Add(setupStatusItem);
            setupMenu.DropDownItems.Add(new ToolStripSeparator());
            setupMenu.DropDownItems.Add(runSetupItem);
            setupMenu.DropDownItems.Add(downloadRclonePortableItem);
            setupMenu.DropDownItems.Add(installRcloneWingetItem);
            setupMenu.DropDownItems.Add(installWinFspWingetItem);
            setupMenu.DropDownItems.Add(configureRemoteItem);
            setupMenu.DropDownItems.Add(openRcloneConfigItem);
            setupMenu.DropDownItems.Add(openWingetHelpItem);

            menu.Items.Add(statusItem);
            if (IsAdministrator()) menu.Items.Add(adminWarningItem);
            menu.Items.Add(storageItem);
            menu.Items.Add(transferQuotaItem);
            menu.Items.Add(sessionItem);
            menu.Items.Add(speedItem);
            menu.Items.Add(limitItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(mountLowItem);
            menu.Items.Add(mountFullItem);
            menu.Items.Add(unmountItem);
            menu.Items.Add(openDriveItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(bandwidthMenu);
            menu.Items.Add(apiKeyMenu);
            menu.Items.Add(setupMenu);
            menu.Items.Add(refreshItem);
            menu.Items.Add(logItem);
            menu.Items.Add(diagnosticsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(startupItem);
            menu.Items.Add(exitItem);

            tray = new NotifyIcon();
            tray.Icon = LoadAppIcon();
            tray.Text = "Pixelpipe";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { if (IsMounted()) Unmount(); else Mount(false); };

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 7000;
            timer.Tick += delegate { QueueRefresh(false, false); };
            timer.Start();

            UpdateMenuText();

            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(900);
                BeginUi(delegate { FirstLaunchSetupIfNeeded(); RefreshDependencyStatusAsync(true); });
            });

            if (HasArg(args, "/automount"))
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(5000);
                    BeginUi(delegate { Mount(false); });
                });
            }
        }

        private ToolStripMenuItem DisabledItem(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Enabled = false;
            return item;
        }

        private void AddBandwidthChoice(string value, string label)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label, null, delegate { SetBandwidth(value); });
            item.Tag = value;
            bandwidthMenu.DropDownItems.Add(item);
            bandwidthChoices.Add(item);
        }

        private bool HasArg(string[] args, string wanted)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], wanted, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private Icon LoadAppIcon()
        {
            string exeIcon = Application.ExecutablePath;
            try
            {
                Icon extracted = Icon.ExtractAssociatedIcon(exeIcon);
                if (extracted != null) return extracted;
            }
            catch { }

            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using (Brush b = new SolidBrush(Color.FromArgb(34, 120, 210))) g.FillEllipse(b, 1, 1, 30, 30);
                using (Pen p = new Pen(Color.White, 2)) g.DrawEllipse(p, 2, 2, 28, 28);
                using (Font f = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush w = new SolidBrush(Color.White)) g.DrawString("P", f, w, 9, 5);
            }
            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        private bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }


        private void FirstLaunchSetupIfNeeded()
        {
            try
            {
                bool firstRun = !String.Equals(LoadSetting("FirstLaunchSetupDone", "0"), "1", StringComparison.OrdinalIgnoreCase);
                bool missingRequired = !RcloneAvailable() || !WinFspInstalled() || !RemoteConfigured();
                if (firstRun || missingRequired)
                {
                    RunFirstLaunchSetup(false);
                    SaveSetting("FirstLaunchSetupDone", "1");
                }
            }
            catch { }
        }

        private void RunFirstLaunchSetup(bool manual)
        {
            try
            {
                StringBuilder intro = new StringBuilder();
                intro.AppendLine("Pixelpipe setup will check:");
                intro.AppendLine();
                intro.AppendLine("- rclone");
                intro.AppendLine("- WinFsp");
                intro.AppendLine("- Pixeldrain: rclone remote");
                intro.AppendLine("- Optional PixelDrain API key for quota display");
                intro.AppendLine();
                intro.AppendLine("Continue?");
                if (MessageBox.Show(intro.ToString(), "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                if (!RcloneAvailable())
                {
                    DialogResult r = MessageBox.Show("rclone was not found.\n\nYes = download portable rclone to your user profile.\nNo = try installing through winget instead.\nCancel = skip rclone setup.",
                                                    "Pixelpipe setup", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) DownloadRclonePortableWithUi();
                    else if (r == DialogResult.No) InstallRcloneWithWinget();
                }

                if (!WinFspInstalled())
                {
                    DialogResult r = MessageBox.Show("WinFsp was not detected. rclone mount needs WinFsp on Windows.\n\nInstall WinFsp using winget now?",
                                                    "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) InstallWinFspWithWinget();
                }

                if (RcloneAvailable() && !RemoteConfigured())
                {
                    DialogResult r = MessageBox.Show("The rclone remote Pixeldrain: was not found.\n\nCreate Pixeldrain: now using your PixelDrain API key?",
                                                    "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) ConfigurePixeldrainRemoteFromPrompt();
                }

                if (!ApiKeyConfigured())
                {
                    DialogResult r = MessageBox.Show("Optional: save a PixelDrain API key for monthly transfer quota display?\n\nThis is stored encrypted with Windows DPAPI for your Windows user only.",
                                                    "Pixelpipe quota setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) SetApiKeyFromPrompt();
                }

                setupStatusText = GetDependencyStatusLine();
                UpdateMenuText();
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
                if (String.Equals(rclonePath, "rclone.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string version = RunProcessCapture("rclone.exe", "version", 3000);
                    return version.IndexOf("rclone", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { }
            return false;
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

        private bool RemoteConfigured()
        {
            try
            {
                if (!RcloneAvailable()) return false;
                string remotes = RunRcloneCapture("listremotes", 5000);
                return remotes.IndexOf("Pixeldrain:", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private void RefreshDependencyStatusAsync(bool force)
        {
            if (dependencyRefreshing) return;
            if (!force && (DateTime.UtcNow - lastDependencyRefreshUtc).TotalSeconds < 30) return;
            dependencyRefreshing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text = GetDependencyStatusLine();
                BeginUi(delegate
                {
                    setupStatusText = text;
                    lastDependencyRefreshUtc = DateTime.UtcNow;
                    dependencyRefreshing = false;
                    UpdateMenuText();
                });
            });
        }

        private string GetDependencyStatusLine()
        {
            bool rclone = RcloneAvailable();
            bool winfsp = WinFspInstalled();
            bool remote = rclone && RemoteConfigured();
            if (rclone && winfsp && remote) return "Setup: ready";
            List<string> missing = new List<string>();
            if (!rclone) missing.Add("rclone");
            if (!winfsp) missing.Add("WinFsp");
            if (rclone && !remote) missing.Add("Pixeldrain: remote");
            return "Setup: missing " + String.Join(", ", missing.ToArray());
        }

        private void DownloadRclonePortableWithUi()
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                string installed = DownloadRclonePortable();
                rclonePath = installed;
                MessageBox.Show("Portable rclone installed here:\n\n" + installed, "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                setupStatusText = GetDependencyStatusLine();
                UpdateMenuText();
            }
            catch (Exception ex)
            {
                DialogResult r = MessageBox.Show("Portable rclone download failed.\n\n" + ex.Message + "\n\nTry winget installation instead?",
                                                "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
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
                MessageBox.Show("winget was not found. Windows installs winget through Microsoft App Installer. The help page will open now.",
                                "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OpenWingetInstallHelp();
                return;
            }
            RunInstallerTerminal("winget install -e --id " + WingetRcloneId + " --accept-package-agreements --accept-source-agreements", false);
        }

        private void InstallWinFspWithWinget()
        {
            if (!WingetAvailable())
            {
                MessageBox.Show("winget was not found. Windows installs winget through Microsoft App Installer. The help page will open now.",
                                "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        private void ConfigurePixeldrainRemoteFromPrompt()
        {
            if (!RcloneAvailable())
            {
                MessageBox.Show("rclone is not available yet.", "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string existing = LoadApiKey();
            string apiKey = PromptForApiKey(existing);
            if (apiKey == null) return;
            apiKey = apiKey.Trim();
            if (apiKey.Length == 0)
            {
                MessageBox.Show("No API key was entered. Pixeldrain: was not configured.", "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string result = RunRcloneCapture("config create Pixeldrain pixeldrain api_key " + QuoteArg(apiKey) + " root_folder_id me --non-interactive", 12000);
            if (result.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 || result.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Older pixeldrain backend examples used directory_id; retry for compatibility.
                result = RunRcloneCapture("config create Pixeldrain pixeldrain api_key " + QuoteArg(apiKey) + " directory_id me --non-interactive", 12000);
            }

            if (RemoteConfigured())
            {
                SaveApiKey(apiKey);
                MessageBox.Show("Pixeldrain: remote configured. The same API key was saved for quota display.",
                                "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                setupStatusText = GetDependencyStatusLine();
                QueueRefresh(true, false);
            }
            else
            {
                MessageBox.Show("rclone did not report Pixeldrain: after configuration.\n\nOutput:\n" + result,
                                "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return "";
                }
                return p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            }
            catch { return ""; }
        }

        private string FindRclonePath()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates = new string[]
            {
                Path.Combine(profile, "Apps", "rclone", "rclone.exe"),
                @"C:\Program Files\rclone-v1.71.1-windows-amd64\rclone.exe",
                @"C:\Program Files\rclone\rclone.exe",
                @"C:\rclone\rclone.exe"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i])) return candidates[i];
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] dirs = path.Split(Path.PathSeparator);
            for (int i = 0; i < dirs.Length; i++)
            {
                try
                {
                    string full = Path.Combine(dirs[i].Trim(), "rclone.exe");
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return "rclone.exe";
        }

        private bool IsMounted()
        {
            if (mountProcess != null)
            {
                try
                {
                    if (!mountProcess.HasExited) return true;
                }
                catch { }
            }
            return false;
        }

        private void Mount(bool fullCache)
        {
            if (IsMounted())
            {
                ShowBalloon("Pixelpipe is already mounted.");
                return;
            }

            rclonePath = FindRclonePath();
            if (!RcloneAvailable())
            {
                DialogResult r = MessageBox.Show("rclone.exe was not found.\n\nDownload the portable Windows rclone build into your user profile now?",
                                                "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) DownloadRclonePortableWithUi();
                if (!RcloneAvailable())
                {
                    MessageBox.Show("rclone is still unavailable. Use Setup / dependencies from the tray menu, or install rclone manually.",
                                    "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (!WinFspInstalled())
            {
                DialogResult r = MessageBox.Show("WinFsp is required for rclone mount on Windows and does not appear to be installed.\n\nInstall WinFsp with winget now?",
                                                "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) InstallWinFspWithWinget();
                MessageBox.Show("After WinFsp finishes installing, mount again from the tray menu. A Windows restart may be required on some systems.",
                                "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!RemoteConfigured())
            {
                DialogResult r = MessageBox.Show("The rclone remote named Pixeldrain: is not configured.\n\nCreate it now using a PixelDrain API key?",
                                                "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) ConfigurePixeldrainRemoteFromPrompt();
                if (!RemoteConfigured())
                {
                    MessageBox.Show("Pixeldrain: is still not configured. The mount cannot start until rclone has a PixelDrain remote.",
                                    "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (IsAdministrator())
            {
                MessageBox.Show("This app is currently running as Administrator.\n\nThe mount may work, but P:\\ can be hidden from normal File Explorer. Exit and run the app normally unless you specifically need an elevated mount.",
                                "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Directory.CreateDirectory(logDir);
            mountUsesFullCache = fullCache;

            string cacheMode = fullCache ? "full" : "writes";
            string args = "mount " + RemoteName + " " + DriveLetter +
                          " --links" +
                          " --network-mode" +
                          " --vfs-cache-mode " + cacheMode +
                          " --dir-cache-time 10m" +
                          " --poll-interval 1m" +
                          " --vfs-write-back 10s" +
                          " --vfs-cache-max-age 6h" +
                          " --vfs-cache-max-size 5G" +
                          " --volname Pixelpipe" +
                          " --rc --rc-no-auth --rc-addr " + RcAddress +
                          " --log-level INFO" +
                          " --log-file " + Quote(logFile);

            if (!String.Equals(selectedBandwidth, "off", StringComparison.OrdinalIgnoreCase))
                args += " --bwlimit " + selectedBandwidth;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = rclonePath;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                mountProcess = Process.Start(psi);
                statusText = "Status: mounting P:\\";
                UpdateMenuText();

                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(1800);
                    BeginUi(delegate
                    {
                        if (mountProcess != null)
                        {
                            try
                            {
                                if (mountProcess.HasExited)
                                {
                                    string tail = TailLog(2200);
                                    statusText = "Status: mount failed";
                                    UpdateMenuText();
                                    MessageBox.Show("rclone exited immediately.\n\nMost likely causes:\n- WinFsp is missing\n- P: is already in use\n- PixelDrain remote is not configured\n- rclone is being forced to run elevated\n\nLog tail:\n" + tail,
                                                    "Pixelpipe mount error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                                else
                                {
                                    statusText = "Status: mounted on P:\\";
                                    ShowBalloon("Pixeldrain mounted on P:\\");
                                    QueueRefresh(true, true);
                                }
                            }
                            catch { }
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                statusText = "Status: mount failed";
                UpdateMenuText();
                MessageBox.Show(ex.Message + "\n\nTry copying rclone.exe to:\n" +
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apps", "rclone", "rclone.exe") +
                                "\n\nAlso check rclone.exe Properties > Compatibility and make sure 'Run this program as administrator' is off.",
                                "Pixelpipe mount error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Unmount()
        {
            try
            {
                if (IsMounted())
                {
                    // Ask rclone to quit cleanly first.
                    RunRcloneCapture("rc core/quit --rc-addr " + RcAddress + " --rc-no-auth", 1500);
                    Thread.Sleep(500);
                    try
                    {
                        if (mountProcess != null && !mountProcess.HasExited) mountProcess.Kill();
                    }
                    catch { }
                }
                statusText = "Status: not mounted";
                speedText = "Current speed: not mounted";
                sessionText = "Session traffic: not mounted";
                UpdateMenuText();
                ShowBalloon("Pixelpipe unmounted.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pixelpipe unmount error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenDrive()
        {
            try
            {
                if (!IsMounted())
                {
                    MessageBox.Show("P:\\ is not mounted by this tray app.\n\nUse Mount Pixelpipe first, then try again. If rclone is running but P:\\ still does not appear, open the rclone log from the tray menu.",
                                    "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Process.Start("explorer.exe", DriveRoot);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetBandwidth(string value)
        {
            selectedBandwidth = value;
            SaveSetting("BandwidthLimit", value);
            UpdateMenuText();

            if (IsMounted())
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string result = RunRcloneCapture("rc core/bwlimit rate=" + value + " --rc-addr " + RcAddress + " --rc-no-auth", 4000);
                    BeginUi(delegate
                    {
                        if (result.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0 || result.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            MessageBox.Show("Could not change the live rclone bandwidth limit. The selected limit will apply next time the mount starts.\n\n" + result,
                                            "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        else
                        {
                            limitItem.Text = "Bandwidth limit: " + DisplayLimit(selectedBandwidth);
                            ShowBalloon("Bandwidth limit: " + DisplayLimit(selectedBandwidth));
                        }
                    });
                });
            }
            else
            {
                ShowBalloon("Bandwidth limit saved for next mount: " + DisplayLimit(selectedBandwidth));
            }
        }

        private void QueueRefresh(bool forceAbout, bool showErrors)
        {
            if (refreshing) return;
            refreshing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string localStatus = null;
                string localStats = null;
                string localSpeed = null;
                string localStorage = null;
                bool mounted = IsMounted();

                if (mounted)
                {
                    string stats = RunRcloneCapture("rc core/stats --rc-addr " + RcAddress + " --rc-no-auth", 3500);
                    if (!String.IsNullOrEmpty(stats))
                    {
                        long bytes = ExtractLong(stats, "bytes");
                        double speed = ExtractDouble(stats, "speed");
                        localStats = "Session traffic: " + FormatBytes(bytes);
                        localSpeed = "Current speed: " + FormatBytes(speed) + "/s";
                    }
                    else
                    {
                        localStats = "Session traffic: unavailable";
                        localSpeed = "Current speed: unavailable";
                    }
                    localStatus = mountUsesFullCache ? "Status: mounted on P:\\ - full cache" : "Status: mounted on P:\\ - low overhead";
                }
                else
                {
                    localStatus = "Status: not mounted";
                    localStats = "Session traffic: not mounted";
                    localSpeed = "Current speed: not mounted";
                }

                bool refreshAbout = forceAbout || (DateTime.UtcNow - lastAboutRefreshUtc).TotalSeconds > 120;
                if (refreshAbout)
                {
                    string about = RunRcloneCapture("about " + RemoteName + " --json", 7000);
                    if (!String.IsNullOrEmpty(about))
                    {
                        long used = ExtractLong(about, "used");
                        long total = ExtractLong(about, "total");
                        long free = ExtractLong(about, "free");
                        if (used >= 0 && total > 0)
                        {
                            localStorage = "Storage: " + FormatBytes(used) + " used / " + FormatBytes(total) + " total";
                        }
                        else if (used >= 0 && free >= 0)
                        {
                            localStorage = "Storage: " + FormatBytes(used) + " used / " + FormatBytes(free) + " free";
                        }
                        else if (about.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || about.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            localStorage = "Storage: unavailable - check rclone config/API access";
                        }
                    }
                    else
                    {
                        localStorage = "Storage: unavailable";
                    }
                    lastAboutRefreshUtc = DateTime.UtcNow;
                }

                bool refreshAccount = forceAbout || (DateTime.UtcNow - lastAccountRefreshUtc).TotalSeconds > 120;
                string localTransferQuota = null;
                if (refreshAccount)
                {
                    localTransferQuota = RefreshPixeldrainTransferQuota();
                    lastAccountRefreshUtc = DateTime.UtcNow;
                }

                BeginUi(delegate
                {
                    if (localStatus != null) statusText = localStatus;
                    if (localStats != null) sessionText = localStats;
                    if (localSpeed != null) speedText = localSpeed;
                    if (localStorage != null) storageText = localStorage;
                    if (localTransferQuota != null) transferQuotaText = localTransferQuota;
                    refreshing = false;
                    UpdateMenuText();
                });
            });
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
            UpdateMenuText();
            QueueRefresh(true, true);
            ShowBalloon("PixelDrain API key saved for quota checks.");
        }

        private string PromptForApiKey(string existing)
        {
            using (Form form = new Form())
            using (Label label = new Label())
            using (TextBox textBox = new TextBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                form.Text = "PixelDrain API key";
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.Width = 520;
                form.Height = 170;

                label.Left = 12;
                label.Top = 12;
                label.Width = 480;
                label.Height = 40;
                label.Text = "Paste a PixelDrain API key. It is stored encrypted for your Windows user only.";

                textBox.Left = 12;
                textBox.Top = 56;
                textBox.Width = 480;
                textBox.UseSystemPasswordChar = true;
                textBox.Text = existing ?? "";
                textBox.SelectAll();

                ok.Text = "Save";
                ok.Left = 316;
                ok.Top = 92;
                ok.Width = 84;
                ok.DialogResult = DialogResult.OK;

                cancel.Text = "Cancel";
                cancel.Left = 408;
                cancel.Top = 92;
                cancel.Width = 84;
                cancel.DialogResult = DialogResult.Cancel;

                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog() == DialogResult.OK ? textBox.Text : null;
            }
        }

        private void ClearApiKey()
        {
            try
            {
                using (RegistryKey key = SettingsKey(true))
                {
                    key.DeleteValue("PixeldrainApiKeyProtected", false);
                }
            }
            catch { }
            transferQuotaText = "Transfer quota: API key not set";
            UpdateMenuText();
            ShowBalloon("PixelDrain API key cleared.");
        }

        private void SaveApiKey(string apiKey)
        {
            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(apiKey ?? "");
                byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                using (RegistryKey key = SettingsKey(true))
                {
                    key.SetValue("PixeldrainApiKeyProtected", Convert.ToBase64String(protectedBytes), RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save API key with Windows DPAPI.\n\n" + ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string LoadApiKey()
        {
            string keyValue = LoadProtectedSetting("PixeldrainApiKeyProtected");
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
            string apiKey = LoadApiKey();
            if (String.IsNullOrWhiteSpace(apiKey)) return "Transfer quota: API key not set";

            try
            {
                string userJson = HttpGetPixeldrain("https://pixeldrain.com/api/user", apiKey, 7000);
                if (String.IsNullOrWhiteSpace(userJson)) return "Transfer quota: unavailable";
                if (userJson.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Transfer quota: API key rejected";

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

                string prefix = "Transfer quota: ";
                if (cap > 0)
                {
                    double pct = cap > 0 ? (double)used * 100.0 / (double)cap : 0.0;
                    long remaining = cap - used;
                    if (remaining < 0) remaining = 0;
                    return prefix + FormatBytes(used) + " / " + FormatBytes(cap) + " used (" + pct.ToString("0.#") + "%, " + FormatBytes(remaining) + " left, 30d)";
                }

                return prefix + FormatBytes(used) + " used in last 30d / no fixed cap";
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
            string token = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + apiKey));

            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 |
                    (SecurityProtocolType)12288; // TLS 1.3 where supported.
                ServicePointManager.Expect100Continue = false;

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "Pixelpipe/1.0";
                req.Headers[HttpRequestHeader.Authorization] = "Basic " + token;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream stream = resp.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                // Preserve real API errors so the caller can still report bad keys correctly.
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
                psi.Arguments = "-fsSL --connect-timeout 8 --max-time " + Math.Max(10, timeoutMs / 1000).ToString() +
                                " -H " + QuoteArg("Authorization: Basic " + basicToken) +
                                " -H " + QuoteArg("User-Agent: Pixelpipe/1.0 curl-fallback") +
                                " " + QuoteArg(url);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                Process p = Process.Start(psi);
                if (p == null) return "";

                string stdout = "";
                string stderr = "";
                if (!p.WaitForExit(timeoutMs + 3000))
                {
                    try { p.Kill(); } catch { }
                    return "";
                }
                stdout = p.StandardOutput.ReadToEnd();
                stderr = p.StandardError.ReadToEnd();
                if (p.ExitCode == 0) return stdout;
                return "";
            }
            catch { return ""; }
        }

        private string FindCurlPath()
        {
            string systemCurl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            if (File.Exists(systemCurl)) return systemCurl;
            return "curl.exe";
        }

        private string QuoteArg(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

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
                StringBuilder output = new StringBuilder();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return "";
                }
                output.Append(p.StandardOutput.ReadToEnd());
                output.Append(p.StandardError.ReadToEnd());
                return output.ToString();
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
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

        private string FormatBytes(double bytes)
        {
            if (bytes < 0) return "unknown";
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024.0 && unit < units.Length - 1)
            {
                value /= 1024.0;
                unit++;
            }
            if (unit == 0) return ((long)value).ToString() + " " + units[unit];
            return value.ToString("0.##") + " " + units[unit];
        }

        private string Quote(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        private string DisplayLimit(string value)
        {
            if (String.IsNullOrEmpty(value) || String.Equals(value, "off", StringComparison.OrdinalIgnoreCase)) return "Unlimited";
            return value + "/s";
        }

        private void UpdateMenuText()
        {
            bool mounted = IsMounted();
            statusItem.Text = statusText;
            storageItem.Text = storageText;
            transferQuotaItem.Text = transferQuotaText;
            sessionItem.Text = sessionText;
            speedItem.Text = speedText;
            limitItem.Text = "Bandwidth limit: " + DisplayLimit(selectedBandwidth);
            mountLowItem.Enabled = !mounted;
            mountFullItem.Enabled = !mounted;
            unmountItem.Enabled = mounted;
            openDriveItem.Enabled = mounted;
            startupItem.Checked = StartupEnabled();
            apiKeyStatusItem.Text = ApiKeyConfigured() ? "API key: configured" : "API key: not set";
            clearApiKeyItem.Enabled = ApiKeyConfigured();
            setupStatusItem.Text = setupStatusText;
            downloadRclonePortableItem.Enabled = true;
            installRcloneWingetItem.Enabled = true;
            installWinFspWingetItem.Enabled = true;
            configureRemoteItem.Enabled = true;
            openRcloneConfigItem.Enabled = true;
            for (int i = 0; i < bandwidthChoices.Count; i++)
            {
                string tag = bandwidthChoices[i].Tag as string;
                bandwidthChoices[i].Checked = String.Equals(tag, selectedBandwidth, StringComparison.OrdinalIgnoreCase);
            }
            tray.Text = mounted ? "Pixelpipe mounted on P:" : "Pixelpipe not mounted";
        }

        private void ShowBalloon(string message)
        {
            try
            {
                tray.BalloonTipTitle = "Pixelpipe";
                tray.BalloonTipText = message;
                tray.ShowBalloonTip(1800);
            }
            catch { }
        }

        private void OpenLog()
        {
            try
            {
                Directory.CreateDirectory(logDir);
                if (!File.Exists(logFile)) File.WriteAllText(logFile, "No rclone log has been written yet.\r\n");
                Process.Start("notepad.exe", Quote(logFile));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string TailLog(int maxChars)
        {
            try
            {
                if (!File.Exists(logFile)) return "No log file exists yet.";
                string s = File.ReadAllText(logFile);
                if (s.Length <= maxChars) return s;
                return s.Substring(s.Length - maxChars);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private void CopyDiagnostics()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Pixelpipe diagnostics");
            sb.AppendLine("Status: " + statusText);
            sb.AppendLine("Storage: " + storageText);
            sb.AppendLine("Transfer quota: " + transferQuotaText);
            sb.AppendLine("API key configured: " + ApiKeyConfigured());
            sb.AppendLine("Session: " + sessionText);
            sb.AppendLine("Speed: " + speedText);
            sb.AppendLine("Bandwidth: " + DisplayLimit(selectedBandwidth));
            sb.AppendLine("Running elevated: " + IsAdministrator());
            sb.AppendLine("rclone path: " + rclonePath);
            sb.AppendLine("mount process running: " + IsMounted());
            sb.AppendLine("drive: " + DriveLetter);
            sb.AppendLine("remote: " + RemoteName);
            sb.AppendLine("rc address: " + RcAddress);
            sb.AppendLine("log file: " + logFile);
            sb.AppendLine();
            sb.AppendLine("Last rclone log tail:");
            sb.AppendLine(TailLog(4000));
            try
            {
                Clipboard.SetText(sb.ToString());
                ShowBalloon("Diagnostics copied to clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private RegistryKey SettingsKey(bool writable)
        {
            if (writable) return Registry.CurrentUser.CreateSubKey(@"Software\" + AppName);
            return Registry.CurrentUser.OpenSubKey(@"Software\" + AppName, false);
        }

        private RegistryKey LegacySettingsKey()
        {
            return Registry.CurrentUser.OpenSubKey(@"Software\" + LegacyAppName, false);
        }

        private string LoadProtectedSetting(string name)
        {
            try
            {
                using (RegistryKey key = SettingsKey(false))
                {
                    object v = key == null ? null : key.GetValue(name);
                    if (v != null) return v.ToString();
                }
            }
            catch { }

            try
            {
                using (RegistryKey key = LegacySettingsKey())
                {
                    object v = key == null ? null : key.GetValue(name);
                    if (v != null) return v.ToString();
                }
            }
            catch { }

            return "";
        }

        private string LoadSetting(string name, string defaultValue)
        {
            string value = LoadProtectedSetting(name);
            return String.IsNullOrEmpty(value) ? defaultValue : value;
        }

        private void SaveSetting(string name, string value)
        {
            try
            {
                using (RegistryKey key = SettingsKey(true))
                {
                    key.SetValue(name, value, RegistryValueKind.String);
                }
            }
            catch { }
        }

        private bool StartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    object value = key.GetValue(AppName);
                    if (value == null) return false;
                    return value.ToString().IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        private void ToggleStartup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (StartupEnabled())
                    {
                        key.DeleteValue(AppName, false);
                        ShowBalloon("Startup auto-mount disabled.");
                    }
                    else
                    {
                        key.SetValue(AppName, Quote(Application.ExecutablePath) + " /automount", RegistryValueKind.String);
                        ShowBalloon("Startup auto-mount enabled.");
                    }
                }
                UpdateMenuText();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pixelpipe startup setting", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExitApp()
        {
            if (IsMounted())
            {
                DialogResult result = MessageBox.Show("Pixelpipe is currently mounted.\n\nUnmount it before exiting?", "Pixelpipe", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Cancel) return;
                if (result == DialogResult.Yes) Unmount();
            }
            tray.Visible = false;
            tray.Dispose();
            Application.Exit();
        }

        private void BeginUi(MethodInvoker action)
        {
            try
            {
                if (tray != null && tray.ContextMenuStrip != null)
                {
                    Control c = tray.ContextMenuStrip;
                    if (c.InvokeRequired) c.BeginInvoke(action);
                    else action();
                }
            }
            catch { }
        }
    }
}
