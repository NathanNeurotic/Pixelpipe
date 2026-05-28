using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Web.Script.Serialization;

namespace Pixelpipe
{
    internal static class Program
    {
        private static void ConfigureModernTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.DefaultConnectionLimit = 16;
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

    internal sealed class RemoteProfile
    {
        public string Id;
        public string Label;
        public string Provider;
        public string Remote;
        public string DriveLetter;
        public string MountMode;
        public bool AutoMount;
        public bool FullCache;

        [ScriptIgnore] public Process MountProcess;
        [ScriptIgnore] public bool DesiredMounted;
        [ScriptIgnore] public int RemountAttempts;
        [ScriptIgnore] public DateTime RemountWindowUtc;
        [ScriptIgnore] public DateTime LastAboutRefreshUtc;
        [ScriptIgnore] public string StatusText;
        [ScriptIgnore] public string StorageText;
        [ScriptIgnore] public string SessionText;
        [ScriptIgnore] public string SpeedText;
        [ScriptIgnore] public string LastError;
        [ScriptIgnore] public int RcPort;
        [ScriptIgnore] public string LogFile;

        public RemoteProfile()
        {
            Id = Guid.NewGuid().ToString("N");
            Label = "Pixeldrain";
            Provider = "pixeldrain";
            Remote = "Pixeldrain:";
            DriveLetter = "P:";
            MountMode = "network";
            AutoMount = false;
            FullCache = false;
            StatusText = "not mounted";
            StorageText = "storage not checked";
            SessionText = "session not mounted";
            SpeedText = "speed not mounted";
            LastError = "";
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private const string AppName = "Pixelpipe";
        private const string LegacyAppName = "PixeldrainAioMountTray";
        private const string DefaultRemoteName = "Pixeldrain:";
        private const string DefaultDriveLetter = "P:";
        private const string RcloneDownloadUrl = "https://downloads.rclone.org/rclone-current-windows-amd64.zip";
        private const string WingetRcloneId = "Rclone.Rclone";
        private const string WingetWinFspId = "WinFsp.WinFsp";
        private const int RcBasePort = 55729;

        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly System.Windows.Forms.Timer timer;
        private readonly List<RemoteProfile> profiles;
        private readonly List<ToolStripMenuItem> bandwidthItems;
        private string selectedBandwidth;
        private string rclonePath;
        private string settingsDir;
        private string settingsFile;
        private string logDir;
        private string setupStatusText;
        private string transferQuotaText;
        private bool refreshing;
        private bool dependencyRefreshing;
        private DateTime lastDependencyRefreshUtc = DateTime.MinValue;
        private DateTime lastQuotaRefreshUtc = DateTime.MinValue;

        public TrayContext(string[] args)
        {
            settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            settingsFile = Path.Combine(settingsDir, "settings.json");
            logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "logs");
            Directory.CreateDirectory(settingsDir);
            Directory.CreateDirectory(logDir);

            rclonePath = FindRclonePath();
            selectedBandwidth = LoadSetting("BandwidthLimit", "off");
            transferQuotaText = ApiKeyConfigured() ? "Transfer quota: not checked" : "Transfer quota: PixelDrain API key not set";
            setupStatusText = "Setup: not checked";
            profiles = LoadProfiles();
            AssignRuntimeFields();

            bandwidthItems = new List<ToolStripMenuItem>();
            menu = new ContextMenuStrip();
            ApplyTrayMenuTheme(menu);
            menu.Opening += delegate { RebuildMenu(); QueueRefresh(false, false); RefreshDependencyStatusAsync(false); };

            tray = new NotifyIcon();
            tray.Icon = LoadAppIcon();
            tray.Text = "Pixelpipe";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { TogglePrimaryProfile(); };

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 7000;
            timer.Tick += delegate { MonitorMountHealth(); QueueRefresh(false, false); };
            timer.Start();

            RebuildMenu();

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
                    BeginUi(delegate { MountAutoProfiles(); });
                });
            }
        }

        private void RebuildMenu()
        {
            menu.Items.Clear();
            AddDisabled("Pixelpipe");
            AddDisabled("Status: " + BuildGlobalStatus());
            if (IsAdministrator()) AddDisabled("Warning: running as Administrator; mounted drives may be hidden from normal Explorer");
            AddDisabled("rclone: " + (RcloneAvailable() ? "found" : "missing"));
            AddDisabled("WinFsp: " + (WinFspInstalled() ? "found" : "missing"));
            AddDisabled(transferQuotaText);
            menu.Items.Add(new ToolStripSeparator());

            for (int i = 0; i < profiles.Count; i++)
            {
                RemoteProfile p = profiles[i];
                ToolStripMenuItem profileMenu = new ToolStripMenuItem(ProfileTitle(p));
                ApplyTrayMenuTheme(profileMenu.DropDown as ContextMenuStrip);
                profileMenu.DropDownItems.Add(DisabledItem("Remote: " + p.Remote));
                profileMenu.DropDownItems.Add(DisabledItem("Drive: " + GetDriveRoot(p)));
                profileMenu.DropDownItems.Add(DisabledItem("Provider: " + DisplayProvider(p.Provider)));
                profileMenu.DropDownItems.Add(DisabledItem("Status: " + p.StatusText));
                profileMenu.DropDownItems.Add(DisabledItem("Storage: " + p.StorageText));
                profileMenu.DropDownItems.Add(DisabledItem("Traffic: " + p.SessionText));
                profileMenu.DropDownItems.Add(DisabledItem("Speed: " + p.SpeedText));
                if (!String.IsNullOrWhiteSpace(p.LastError)) profileMenu.DropDownItems.Add(DisabledItem("Last error: " + TrimForMenu(p.LastError, 90)));
                profileMenu.DropDownItems.Add(new ToolStripSeparator());
                profileMenu.DropDownItems.Add(new ToolStripMenuItem("Mount - low overhead", null, delegate { MountProfile(p, false); }) { Enabled = !IsMounted(p) });
                profileMenu.DropDownItems.Add(new ToolStripMenuItem("Mount - full cache", null, delegate { MountProfile(p, true); }) { Enabled = !IsMounted(p) });
                profileMenu.DropDownItems.Add(new ToolStripMenuItem("Unmount", null, delegate { UnmountProfile(p, false); }) { Enabled = IsMounted(p) });
                profileMenu.DropDownItems.Add(new ToolStripMenuItem("Open " + GetDriveRoot(p), null, delegate { OpenDrive(p); }) { Enabled = IsMounted(p) });
                profileMenu.DropDownItems.Add(new ToolStripSeparator());
                profileMenu.DropDownItems.Add(new ToolStripMenuItem("Edit profile...", null, delegate { EditProfile(p); }) { Enabled = !IsMounted(p) });
                profileMenu.DropDownItems.Add(new ToolStripMenuItem("Set as primary", null, delegate { MakePrimaryProfile(p); }));
                profileMenu.DropDownItems.Add(new ToolStripMenuItem("Auto-mount this profile", null, delegate { ToggleProfileAutoMount(p); }) { Checked = p.AutoMount });
                profileMenu.DropDownItems.Add(new ToolStripMenuItem("Remove profile", null, delegate { RemoveProfile(p); }) { Enabled = !IsMounted(p) && profiles.Count > 1 });
                menu.Items.Add(profileMenu);
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(BuildAddRemoteMenu());
            menu.Items.Add(BuildBandwidthMenu());
            menu.Items.Add(BuildSetupMenu());
            menu.Items.Add(new ToolStripMenuItem("Import existing rclone remotes", null, delegate { ImportExistingRemotes(); }));
            menu.Items.Add(new ToolStripMenuItem("Manage remotes...", null, delegate { ShowManageRemotesWindow(); }));
            menu.Items.Add(new ToolStripMenuItem("Diagnostics / repair...", null, delegate { ShowDiagnosticsWindow(); }));
            menu.Items.Add(new ToolStripMenuItem("Settings file", null, delegate { OpenSettingsFile(); }));
            menu.Items.Add(new ToolStripMenuItem("Open log folder", null, delegate { OpenLogFolder(); }));
            menu.Items.Add(new ToolStripMenuItem("Copy diagnostics", null, delegate { CopyDiagnostics(); }));
            menu.Items.Add(new ToolStripMenuItem("Refresh usage now", null, delegate { QueueRefresh(true, true); }));
            menu.Items.Add(new ToolStripMenuItem("Check for updates", null, delegate { CheckForUpdates(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Auto-mount at Windows startup", null, delegate { ToggleStartup(); }) { Checked = StartupEnabled() });
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitApp(); }));

            tray.Text = AnyMounted() ? "Pixelpipe mounted" : "Pixelpipe not mounted";
        }

        private ToolStripMenuItem BuildAddRemoteMenu()
        {
            ToolStripMenuItem add = new ToolStripMenuItem("Add cloud remote");
            add.DropDownItems.Add(new ToolStripMenuItem("Pixeldrain", null, delegate { AddPixeldrainProfile(); }));
            add.DropDownItems.Add(new ToolStripMenuItem("Google Drive", null, delegate { AddGuidedRcloneRemote("Google Drive", "drive", "G:"); }));
            add.DropDownItems.Add(new ToolStripMenuItem("MEGA", null, delegate { AddGuidedRcloneRemote("MEGA", "mega", "M:"); }));
            add.DropDownItems.Add(new ToolStripMenuItem("OneDrive", null, delegate { AddGuidedRcloneRemote("OneDrive", "onedrive", "O:"); }));
            add.DropDownItems.Add(new ToolStripMenuItem("Dropbox", null, delegate { AddGuidedRcloneRemote("Dropbox", "dropbox", "D:"); }));
            add.DropDownItems.Add(new ToolStripMenuItem("Box", null, delegate { AddGuidedRcloneRemote("Box", "box", "B:"); }));
            add.DropDownItems.Add(new ToolStripMenuItem("S3 / R2 / B2 / Wasabi", null, delegate { AddGuidedRcloneRemote("S3-compatible", "s3", "R:"); }));
            add.DropDownItems.Add(new ToolStripMenuItem("WebDAV / Nextcloud", null, delegate { AddGuidedRcloneRemote("WebDAV", "webdav", "W:"); }));
            add.DropDownItems.Add(new ToolStripMenuItem("SFTP", null, delegate { AddGuidedRcloneRemote("SFTP", "sftp", "S:"); }));
            add.DropDownItems.Add(new ToolStripSeparator());
            add.DropDownItems.Add(new ToolStripMenuItem("Custom existing rclone remote...", null, delegate { AddExistingRemoteProfile(); }));
            add.DropDownItems.Add(new ToolStripMenuItem("Open rclone config terminal", null, delegate { OpenRcloneConfigTerminal(); }));
            return add;
        }

        private ToolStripMenuItem BuildBandwidthMenu()
        {
            bandwidthItems.Clear();
            ToolStripMenuItem m = new ToolStripMenuItem("Bandwidth limit: " + DisplayLimit(selectedBandwidth));
            AddBandwidthChoice(m, "off", "Unlimited");
            AddBandwidthChoice(m, "512K", "512 KB/s");
            AddBandwidthChoice(m, "1M", "1 MB/s");
            AddBandwidthChoice(m, "5M", "5 MB/s");
            AddBandwidthChoice(m, "10M", "10 MB/s");
            AddBandwidthChoice(m, "25M", "25 MB/s");
            AddBandwidthChoice(m, "50M", "50 MB/s");
            AddBandwidthChoice(m, "100M", "100 MB/s");
            AddBandwidthChoice(m, "250M", "250 MB/s");
            m.DropDownItems.Add(new ToolStripSeparator());
            m.DropDownItems.Add(new ToolStripMenuItem("Custom...", null, delegate { SetCustomBandwidth(); }));
            return m;
        }

        private ToolStripMenuItem BuildSetupMenu()
        {
            ToolStripMenuItem setup = new ToolStripMenuItem("Setup / dependencies");
            setup.DropDownItems.Add(DisabledItem(setupStatusText));
            setup.DropDownItems.Add(new ToolStripSeparator());
            setup.DropDownItems.Add(new ToolStripMenuItem("Run first-time setup wizard", null, delegate { RunFirstLaunchSetup(true); }));
            setup.DropDownItems.Add(new ToolStripMenuItem("Download portable rclone now", null, delegate { DownloadRclonePortableWithUi(); }));
            setup.DropDownItems.Add(new ToolStripMenuItem("Install/update rclone with winget", null, delegate { InstallRcloneWithWinget(); }));
            setup.DropDownItems.Add(new ToolStripMenuItem("Install WinFsp with winget", null, delegate { InstallWinFspWithWinget(); }));
            setup.DropDownItems.Add(new ToolStripMenuItem("Configure Pixeldrain remote", null, delegate { ConfigurePixeldrainRemoteFromPrompt(GetPrimaryProfile()); }));
            setup.DropDownItems.Add(new ToolStripMenuItem("Open rclone config in terminal", null, delegate { OpenRcloneConfigTerminal(); }));
            setup.DropDownItems.Add(new ToolStripMenuItem("Open winget/App Installer help", null, delegate { OpenWingetInstallHelp(); }));
            return setup;
        }

        private void AddDisabled(string text)
        {
            menu.Items.Add(DisabledItem(text));
        }

        private ToolStripMenuItem DisabledItem(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Enabled = false;
            return item;
        }

        private void AddBandwidthChoice(ToolStripMenuItem parent, string value, string label)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label, null, delegate { SetBandwidth(value); });
            item.Tag = value;
            item.Checked = String.Equals(selectedBandwidth, value, StringComparison.OrdinalIgnoreCase);
            parent.DropDownItems.Add(item);
            bandwidthItems.Add(item);
        }

        private void AssignRuntimeFields()
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                RemoteProfile p = profiles[i];
                if (String.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString("N");
                if (String.IsNullOrWhiteSpace(p.Label)) p.Label = RemoteNameBare(p.Remote);
                p.Remote = NormalizeRemoteName(p.Remote);
                p.DriveLetter = NormalizeDriveLetter(p.DriveLetter);
                p.MountMode = NormalizeMountMode(p.MountMode);
                p.Provider = NormalizeProvider(p.Provider, p.Remote);
                p.RcPort = ProfilePort(p);
                p.StatusText = "not mounted";
                p.StorageText = "storage not checked";
                p.SessionText = "session not mounted";
                p.SpeedText = "speed not mounted";
                p.LastError = "";
                p.LogFile = Path.Combine(logDir, SafeFileName(p.Label + "-" + p.DriveLetter.Replace(":", "")) + ".log");
            }
        }

        private int ProfilePort(RemoteProfile p)
        {
            string id = p == null || String.IsNullOrWhiteSpace(p.Id) ? Guid.NewGuid().ToString("N") : p.Id;
            int h = 0;
            for (int i = 0; i < id.Length; i++) h = ((h * 31) + id[i]) & 0x7fffffff;
            return RcBasePort + (h % 7000);
        }

        private string ProfileTitle(RemoteProfile p)
        {
            return p.Label + "  [" + p.DriveLetter + "]  " + (IsMounted(p) ? "mounted" : "unmounted");
        }

        private string BuildGlobalStatus()
        {
            int mounted = 0;
            for (int i = 0; i < profiles.Count; i++) if (IsMounted(profiles[i])) mounted++;
            if (mounted == 0) return "no remotes mounted";
            return mounted.ToString() + " of " + profiles.Count.ToString() + " remotes mounted";
        }

        private void FirstLaunchSetupIfNeeded()
        {
            try
            {
                bool firstRun = !String.Equals(LoadSetting("FirstLaunchSetupDone", "0"), "1", StringComparison.OrdinalIgnoreCase);
                bool missingRequired = !RcloneAvailable() || !WinFspInstalled() || !AnyRemoteConfigured();
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
                intro.AppendLine("- Pixeldrain or another rclone remote");
                intro.AppendLine("- Optional PixelDrain API key for quota display");
                intro.AppendLine();
                intro.AppendLine("Pixelpipe now supports Pixeldrain plus other rclone-compatible remotes, including Google Drive, MEGA, OneDrive, Dropbox, Box, S3-compatible storage, WebDAV, SFTP, and custom rclone remotes.");
                intro.AppendLine();
                intro.AppendLine("Continue?");
                if (MessageBox.Show(intro.ToString(), "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

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
            for (int i = 0; i < profiles.Count; i++) if (RemoteConfigured(profiles[i])) return true;
            return false;
        }

        private bool RemoteConfigured(RemoteProfile p)
        {
            try
            {
                if (p == null || !RcloneAvailable()) return false;
                string remotes = RunRcloneCapture("listremotes", 6000);
                return remotes.IndexOf(NormalizeRemoteName(p.Remote), StringComparison.OrdinalIgnoreCase) >= 0;
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
                    RebuildMenu();
                });
            });
        }

        private string GetDependencyStatusLine()
        {
            bool rclone = RcloneAvailable();
            bool winfsp = WinFspInstalled();
            bool remote = rclone && AnyRemoteConfigured();
            if (rclone && winfsp && remote) return "Setup: ready";
            List<string> missing = new List<string>();
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

        private void AddPixeldrainProfile()
        {
            RemoteProfile p = new RemoteProfile();
            p.Label = UniqueLabel("Pixeldrain");
            p.Provider = "pixeldrain";
            p.Remote = UniqueRemoteName("Pixeldrain") + ":";
            p.DriveLetter = FirstFreePreferredDrive("P:");
            p.MountMode = "network";
            profiles.Add(p);
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
            profiles.Add(p);
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
            profiles.Add(p);
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
                profiles.Add(p);
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
                List<string> result = new List<string>();
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
            catch { }
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
                MessageBox.Show("rclone did not report " + p.Remote + " after configuration.\r\n\r\nOutput:\r\n" + result, "Pixelpipe setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private void MountAutoProfiles()
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].AutoMount) MountProfile(profiles[i], profiles[i].FullCache);
            }
            if (!AnyMounted() && profiles.Count > 0) MountProfile(profiles[0], profiles[0].FullCache);
        }

        private void TogglePrimaryProfile()
        {
            RemoteProfile p = GetPrimaryProfile();
            if (p == null) return;
            if (IsMounted(p)) UnmountProfile(p, false);
            else MountProfile(p, false);
        }

        private RemoteProfile GetPrimaryProfile()
        {
            if (profiles.Count == 0)
            {
                profiles.Add(new RemoteProfile());
                AssignRuntimeFields();
            }
            return profiles[0];
        }

        private void MakePrimaryProfile(RemoteProfile p)
        {
            if (p == null || profiles.Count < 2) return;
            profiles.Remove(p);
            profiles.Insert(0, p);
            AssignRuntimeFields();
            SaveProfiles();
            RebuildMenu();
            ShowBalloon(p.Label + " set as primary.");
        }

        private bool AnyMounted()
        {
            for (int i = 0; i < profiles.Count; i++) if (IsMounted(profiles[i])) return true;
            return false;
        }

        private bool IsMounted(RemoteProfile p)
        {
            if (p == null || p.MountProcess == null) return false;
            try { return !p.MountProcess.HasExited; } catch { return false; }
        }

        private void MountProfile(RemoteProfile p, bool fullCache)
        {
            if (p == null) return;
            if (IsMounted(p))
            {
                ShowBalloon(p.Label + " is already mounted.");
                return;
            }

            rclonePath = FindRclonePath();
            if (!RcloneAvailable())
            {
                DialogResult r = MessageBox.Show("rclone.exe was not found.\r\n\r\nDownload the portable Windows rclone build into your user profile now?", "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) DownloadRclonePortableWithUi();
                if (!RcloneAvailable())
                {
                    MessageBox.Show("rclone is still unavailable. Use Setup / dependencies from the tray menu, or install rclone manually.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (!WinFspInstalled())
            {
                DialogResult r = MessageBox.Show("WinFsp is required for rclone mount on Windows and does not appear to be installed.\r\n\r\nInstall WinFsp with winget now?", "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) InstallWinFspWithWinget();
                MessageBox.Show("After WinFsp finishes installing, mount again from the tray menu. A Windows restart may be required on some systems.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!RemoteConfigured(p))
            {
                DialogResult r;
                if (String.Equals(p.Provider, "pixeldrain", StringComparison.OrdinalIgnoreCase))
                {
                    r = MessageBox.Show(p.Remote + " is not configured in rclone.\r\n\r\nCreate it now using a PixelDrain API key?", "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) ConfigurePixeldrainRemoteFromPrompt(p);
                }
                else
                {
                    r = MessageBox.Show(p.Remote + " is not configured in rclone.\r\n\r\nOpen rclone config now?", "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) OpenRcloneConfigTerminal();
                }
                if (!RemoteConfigured(p))
                {
                    MessageBox.Show("The selected rclone remote is still not configured. The mount cannot start until rclone has this remote.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (DriveLetterInUse(p.DriveLetter))
            {
                DialogResult r = MessageBox.Show(p.DriveLetter + " appears to already be in use. Continue anyway?", "Pixelpipe", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            if (IsAdministrator())
            {
                MessageBox.Show("This app is currently running as Administrator.\r\n\r\nThe mount may work, but the drive can be hidden from normal File Explorer. Exit and run the app normally unless you specifically need an elevated mount.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Directory.CreateDirectory(logDir);
            p.FullCache = fullCache;
            p.DesiredMounted = true;
            p.LastError = "";
            string cacheMode = fullCache ? "full" : "writes";
            string args = "mount " + NormalizeRemoteName(p.Remote) + " " + NormalizeDriveLetter(p.DriveLetter) +
                          " --links" +
                          (String.Equals(p.MountMode, "network", StringComparison.OrdinalIgnoreCase) ? " --network-mode" : "") +
                          " --vfs-cache-mode " + cacheMode +
                          " --dir-cache-time 10m" +
                          " --poll-interval 1m" +
                          " --vfs-write-back 10s" +
                          " --vfs-cache-max-age 6h" +
                          " --vfs-cache-max-size 5G" +
                          " --volname " + QuoteArg(p.Label) +
                          " --rc --rc-no-auth --rc-addr 127.0.0.1:" + p.RcPort.ToString() +
                          " --log-level INFO" +
                          " --log-file " + Quote(p.LogFile);

            if (!String.Equals(selectedBandwidth, "off", StringComparison.OrdinalIgnoreCase)) args += " --bwlimit " + selectedBandwidth;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = rclonePath;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                p.MountProcess = Process.Start(psi);
                p.StatusText = "mounting " + GetDriveRoot(p);
                SaveProfiles();
                RebuildMenu();

                ThreadPool.QueueUserWorkItem(delegate
                {
                    Thread.Sleep(1900);
                    BeginUi(delegate
                    {
                        if (p.MountProcess != null)
                        {
                            try
                            {
                                if (p.MountProcess.HasExited)
                                {
                                    string tail = TailLog(p, 2400);
                                    p.StatusText = "mount failed";
                                    p.LastError = tail;
                                    RebuildMenu();
                                    MessageBox.Show("rclone exited immediately.\r\n\r\nMost likely causes:\r\n- WinFsp is missing\r\n- selected drive letter is already in use\r\n- selected rclone remote is not configured\r\n- rclone is being forced to run elevated\r\n\r\nLog tail:\r\n" + tail, "Pixelpipe mount error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                                else
                                {
                                    p.StatusText = "mounted on " + GetDriveRoot(p) + (fullCache ? " - full cache" : " - low overhead");
                                    ShowBalloon(p.Label + " mounted on " + GetDriveRoot(p));
                                    QueueRefresh(true, true);
                                    RebuildMenu();
                                }
                            }
                            catch { }
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                p.StatusText = "mount failed";
                p.LastError = ex.Message;
                RebuildMenu();
                MessageBox.Show(ex.Message + "\r\n\r\nTry copying rclone.exe to:\r\n" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apps", "rclone", "rclone.exe") + "\r\n\r\nAlso check rclone.exe Properties > Compatibility and make sure 'Run this program as administrator' is off.", "Pixelpipe mount error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UnmountProfile(RemoteProfile p, bool silent)
        {
            if (p == null) return;
            try
            {
                p.DesiredMounted = false;
                if (IsMounted(p))
                {
                    RunRcloneCapture("rc mount/unmount mountPoint=" + p.DriveLetter + " --rc-addr 127.0.0.1:" + p.RcPort.ToString() + " --rc-no-auth", 2500);
                    Thread.Sleep(600);
                    if (IsMounted(p)) RunRcloneCapture("rc core/quit --rc-addr 127.0.0.1:" + p.RcPort.ToString() + " --rc-no-auth", 2500);
                    Thread.Sleep(900);
                    if (IsMounted(p))
                    {
                        DialogResult force = MessageBox.Show(p.Label + " did not exit after a clean unmount request.\r\n\r\nForce-kill it now? This is a last resort.", "Pixelpipe unmount", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (force == DialogResult.Yes)
                        {
                            try { if (p.MountProcess != null && !p.MountProcess.HasExited) p.MountProcess.Kill(); } catch { }
                        }
                        else
                        {
                            p.StatusText = "unmount still pending";
                            RebuildMenu();
                            return;
                        }
                    }
                }
                CleanStaleDriveMappings(p, false);
                p.StatusText = "not mounted";
                p.SpeedText = "speed not mounted";
                p.SessionText = "session not mounted";
                p.MountProcess = null;
                RebuildMenu();
                if (!silent) ShowBalloon(p.Label + " unmounted.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pixelpipe unmount error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenDrive(RemoteProfile p)
        {
            try
            {
                if (!IsMounted(p))
                {
                    MessageBox.Show(GetDriveRoot(p) + " is not mounted by this tray app.\r\n\r\nMount it first, then try again. If rclone is running but the drive still does not appear, open the rclone log from the tray menu.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Process.Start("explorer.exe", GetDriveRoot(p));
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SetBandwidth(string value)
        {
            selectedBandwidth = String.IsNullOrWhiteSpace(value) ? "off" : value.Trim();
            SaveSetting("BandwidthLimit", selectedBandwidth);
            RebuildMenu();

            bool any = false;
            for (int i = 0; i < profiles.Count; i++)
            {
                RemoteProfile p = profiles[i];
                if (!IsMounted(p)) continue;
                any = true;
                ThreadPool.QueueUserWorkItem(delegate(object state)
                {
                    RemoteProfile profile = (RemoteProfile)state;
                    string result = RunRcloneCapture("rc core/bwlimit rate=" + selectedBandwidth + " --rc-addr 127.0.0.1:" + profile.RcPort.ToString() + " --rc-no-auth", 4000);
                    if (result.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0 || result.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) profile.LastError = result;
                }, p);
            }
            ShowBalloon(any ? "Bandwidth limit applied: " + DisplayLimit(selectedBandwidth) : "Bandwidth limit saved for next mount: " + DisplayLimit(selectedBandwidth));
        }

        private void QueueRefresh(bool forceAbout, bool showErrors)
        {
            if (refreshing) return;
            refreshing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < profiles.Count; i++) RefreshProfile(profiles[i], forceAbout);

                bool refreshQuota = forceAbout || (DateTime.UtcNow - lastQuotaRefreshUtc).TotalSeconds > 120;
                if (refreshQuota)
                {
                    transferQuotaText = RefreshPixeldrainTransferQuota();
                    lastQuotaRefreshUtc = DateTime.UtcNow;
                }

                BeginUi(delegate
                {
                    refreshing = false;
                    RebuildMenu();
                });
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
                    p.SessionText = FormatBytes(bytes);
                    p.SpeedText = FormatBytes(speed) + "/s";
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
                p.StatusText = "not mounted";
                p.SessionText = "not mounted";
                p.SpeedText = "not mounted";
            }

            bool refreshAbout = forceAbout || (DateTime.UtcNow - p.LastAboutRefreshUtc).TotalSeconds > 120;
            if (refreshAbout)
            {
                string about = RunRcloneCapture("about " + NormalizeRemoteName(p.Remote) + " --json", 8000);
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
            using (Label label = new Label())
            using (TextBox textBox = new TextBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                label.Left = 12; label.Top = 12; label.Width = 500; label.Height = 44;
                label.Text = "Paste a PixelDrain API key. It is stored encrypted for your Windows user only.";
                label.ForeColor = Color.WhiteSmoke;
                textBox.Left = 12; textBox.Top = 62; textBox.Width = 500; textBox.UseSystemPasswordChar = true; textBox.Text = existing ?? ""; textBox.SelectAll();
                ok.Text = "Save"; ok.Left = 336; ok.Top = 104; ok.Width = 84; ok.DialogResult = DialogResult.OK;
                cancel.Text = "Cancel"; cancel.Left = 428; cancel.Top = 104; cancel.Width = 84; cancel.DialogResult = DialogResult.Cancel;
                form.Controls.Add(label); form.Controls.Add(textBox); form.Controls.Add(ok); form.Controls.Add(cancel);
                form.AcceptButton = ok; form.CancelButton = cancel;
                return form.ShowDialog() == DialogResult.OK ? textBox.Text : null;
            }
        }

        private void ClearApiKey()
        {
            try { DeleteSetting("PixeldrainApiKeyProtected"); } catch { }
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
            for (int i = 0; i < profiles.Count; i++) if (String.Equals(profiles[i].Provider, "pixeldrain", StringComparison.OrdinalIgnoreCase)) hasPixeldrain = true;
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
            string token = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + apiKey));
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
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

        private string FindCurlPath()
        {
            string systemCurl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            if (File.Exists(systemCurl)) return systemCurl;
            return "curl.exe";
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
            for (int i = 0; i < candidates.Length; i++) if (File.Exists(candidates[i])) return candidates[i];
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
            while (value >= 1024.0 && unit < units.Length - 1) { value /= 1024.0; unit++; }
            if (unit == 0) return ((long)value).ToString() + " " + units[unit];
            return value.ToString("0.##") + " " + units[unit];
        }

        private string DisplayLimit(string value)
        {
            if (String.IsNullOrEmpty(value) || String.Equals(value, "off", StringComparison.OrdinalIgnoreCase)) return "Unlimited";
            return value + "/s";
        }

        private void MonitorMountHealth()
        {
            try
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    RemoteProfile p = profiles[i];
                    if (!p.AutoMount || !p.DesiredMounted || p.MountProcess == null) continue;
                    bool exited = false;
                    try { exited = p.MountProcess.HasExited; } catch { exited = true; }
                    if (!exited) continue;
                    DateTime now = DateTime.UtcNow;
                    if ((now - p.RemountWindowUtc).TotalMinutes > 5)
                    {
                        p.RemountWindowUtc = now;
                        p.RemountAttempts = 0;
                    }
                    p.RemountAttempts++;
                    if (p.RemountAttempts > 3)
                    {
                        p.DesiredMounted = false;
                        p.StatusText = "auto-remount stopped after repeated failures";
                        ShowBalloon(p.Label + " auto-remount stopped after repeated failures.");
                        continue;
                    }
                    p.StatusText = "rclone exited; auto-remounting";
                    MountProfile(p, p.FullCache);
                }
            }
            catch { }
        }

        private void CleanStaleDriveMappings(RemoteProfile p, bool show)
        {
            try { RunProcessCapture("cmd.exe", "/c net use " + p.DriveLetter + " /delete /y", 2500); } catch { }
            try { RunProcessCapture("mountvol.exe", p.DriveLetter + " /D", 2500); } catch { }
            if (show) ShowBalloon("Stale mapping cleanup attempted for " + p.DriveLetter);
        }

        private bool DriveLetterInUse(string letter)
        {
            try
            {
                string root = NormalizeDriveLetter(letter) + "\\";
                if (Directory.Exists(root)) return true;
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++) if (String.Equals(drives[i].Name.TrimEnd('\\'), NormalizeDriveLetter(letter), StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
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
            profiles.Remove(p);
            if (profiles.Count == 0) profiles.Add(new RemoteProfile());
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
            form.Show();
        }

        private void ShowDiagnosticsWindow()
        {
            Form form = new Form();
            form.Text = "Pixelpipe diagnostics / repair";
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Width = 820;
            form.Height = 580;
            form.BackColor = Color.FromArgb(18, 22, 28);
            form.ForeColor = Color.WhiteSmoke;
            TextBox box = new TextBox();
            box.Multiline = true; box.ReadOnly = true; box.ScrollBars = ScrollBars.Vertical; box.Font = new Font("Consolas", 9f);
            box.Left = 12; box.Top = 12; box.Width = 780; box.Height = 380; box.Text = BuildDiagnosticsText();
            Button refresh = new Button(); refresh.Text = "Refresh"; refresh.Left = 12; refresh.Top = 410; refresh.Width = 90; refresh.Click += delegate { box.Text = BuildDiagnosticsText(); };
            Button copy = new Button(); copy.Text = "Copy"; copy.Left = 110; copy.Top = 410; copy.Width = 90; copy.Click += delegate { Clipboard.SetText(box.Text); };
            Button installRclone = new Button(); installRclone.Text = "Install rclone"; installRclone.Left = 208; installRclone.Top = 410; installRclone.Width = 110; installRclone.Click += delegate { DownloadRclonePortableWithUi(); box.Text = BuildDiagnosticsText(); };
            Button installWinFsp = new Button(); installWinFsp.Text = "Install WinFsp"; installWinFsp.Left = 326; installWinFsp.Top = 410; installWinFsp.Width = 110; installWinFsp.Click += delegate { InstallWinFspWithWinget(); };
            Button configRemote = new Button(); configRemote.Text = "rclone config"; configRemote.Left = 444; configRemote.Top = 410; configRemote.Width = 110; configRemote.Click += delegate { OpenRcloneConfigTerminal(); };
            Button cleanup = new Button(); cleanup.Text = "Clear stale primary drive"; cleanup.Left = 562; cleanup.Top = 410; cleanup.Width = 150; cleanup.Click += delegate { CleanStaleDriveMappings(GetPrimaryProfile(), true); box.Text = BuildDiagnosticsText(); };
            Button restart = new Button(); restart.Text = "Restart primary"; restart.Left = 12; restart.Top = 450; restart.Width = 120; restart.Click += delegate { RemoteProfile p = GetPrimaryProfile(); bool full = p.FullCache; UnmountProfile(p, true); MountProfile(p, full); };
            Button logs = new Button(); logs.Text = "Open logs"; logs.Left = 140; logs.Top = 450; logs.Width = 100; logs.Click += delegate { OpenLogFolder(); };
            Button settings = new Button(); settings.Text = "Open settings"; settings.Left = 248; settings.Top = 450; settings.Width = 110; settings.Click += delegate { OpenSettingsFile(); };
            Button close = new Button(); close.Text = "Close"; close.Left = 702; close.Top = 490; close.Width = 90; close.Click += delegate { form.Close(); };
            form.Controls.Add(box); form.Controls.Add(refresh); form.Controls.Add(copy); form.Controls.Add(installRclone); form.Controls.Add(installWinFsp); form.Controls.Add(configRemote); form.Controls.Add(cleanup); form.Controls.Add(restart); form.Controls.Add(logs); form.Controls.Add(settings); form.Controls.Add(close);
            form.Show();
        }

        private string BuildDiagnosticsText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Pixelpipe diagnostics");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Running elevated: " + IsAdministrator());
            sb.AppendLine("rclone available: " + RcloneAvailable());
            sb.AppendLine("rclone path: " + rclonePath);
            sb.AppendLine("WinFsp installed: " + WinFspInstalled());
            sb.AppendLine("configured profiles: " + profiles.Count.ToString());
            sb.AppendLine("any remote configured: " + AnyRemoteConfigured());
            sb.AppendLine("bandwidth: " + DisplayLimit(selectedBandwidth));
            sb.AppendLine("PixelDrain API key configured: " + ApiKeyConfigured());
            sb.AppendLine("transfer quota: " + transferQuotaText);
            sb.AppendLine("settings file: " + settingsFile);
            sb.AppendLine("log dir: " + logDir);
            sb.AppendLine();
            for (int i = 0; i < profiles.Count; i++)
            {
                RemoteProfile p = profiles[i];
                sb.AppendLine("Profile " + (i + 1).ToString() + ": " + p.Label);
                sb.AppendLine("  provider: " + p.Provider);
                sb.AppendLine("  remote: " + p.Remote);
                sb.AppendLine("  drive: " + p.DriveLetter);
                sb.AppendLine("  mode: " + p.MountMode);
                sb.AppendLine("  auto-mount: " + p.AutoMount);
                sb.AppendLine("  remote configured: " + RemoteConfigured(p));
                sb.AppendLine("  mounted by Pixelpipe: " + IsMounted(p));
                sb.AppendLine("  rc: 127.0.0.1:" + p.RcPort.ToString());
                sb.AppendLine("  status: " + p.StatusText);
                sb.AppendLine("  storage: " + p.StorageText);
                sb.AppendLine("  session: " + p.SessionText);
                sb.AppendLine("  speed: " + p.SpeedText);
                sb.AppendLine("  log: " + p.LogFile);
                if (!String.IsNullOrWhiteSpace(p.LastError)) sb.AppendLine("  last error: " + p.LastError);
                sb.AppendLine("  log tail:");
                sb.AppendLine(TailLog(p, 2000));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private void CopyDiagnostics()
        {
            try
            {
                Clipboard.SetText(BuildDiagnosticsText());
                ShowBalloon("Diagnostics copied to clipboard.");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private List<RemoteProfile> LoadProfiles()
        {
            List<RemoteProfile> result = new List<RemoteProfile>();
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                object profilesObj;
                if (root.TryGetValue("Profiles", out profilesObj))
                {
                    object[] arr = profilesObj as object[];
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            Dictionary<string, object> d = arr[i] as Dictionary<string, object>;
                            if (d == null) continue;
                            RemoteProfile p = new RemoteProfile();
                            p.Id = ToStringValue(GetDictValue(d, "Id"), Guid.NewGuid().ToString("N"));
                            p.Label = ToStringValue(GetDictValue(d, "Label"), "Remote");
                            p.Provider = ToStringValue(GetDictValue(d, "Provider"), "custom");
                            p.Remote = ToStringValue(GetDictValue(d, "Remote"), DefaultRemoteName);
                            p.DriveLetter = ToStringValue(GetDictValue(d, "DriveLetter"), DefaultDriveLetter);
                            p.MountMode = ToStringValue(GetDictValue(d, "MountMode"), "network");
                            p.AutoMount = ToBool(GetDictValue(d, "AutoMount"));
                            p.FullCache = ToBool(GetDictValue(d, "FullCache"));
                            result.Add(p);
                        }
                    }
                }
            }
            catch { }

            if (result.Count == 0)
            {
                RemoteProfile p = new RemoteProfile();
                p.Label = "Pixeldrain";
                p.Provider = "pixeldrain";
                p.Remote = NormalizeRemoteName(LoadSetting("RemoteName", DefaultRemoteName));
                p.DriveLetter = NormalizeDriveLetter(LoadSetting("DriveLetter", DefaultDriveLetter));
                p.MountMode = NormalizeMountMode(LoadSetting("MountMode", "network"));
                p.AutoMount = false;
                result.Add(p);
            }
            return result;
        }

        private void SaveProfiles()
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                List<object> list = new List<object>();
                for (int i = 0; i < profiles.Count; i++)
                {
                    RemoteProfile p = profiles[i];
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["Id"] = p.Id;
                    d["Label"] = p.Label;
                    d["Provider"] = p.Provider;
                    d["Remote"] = NormalizeRemoteName(p.Remote);
                    d["DriveLetter"] = NormalizeDriveLetter(p.DriveLetter);
                    d["MountMode"] = NormalizeMountMode(p.MountMode);
                    d["AutoMount"] = p.AutoMount;
                    d["FullCache"] = p.FullCache;
                    list.Add(d);
                }
                root["Profiles"] = list.ToArray();
                root["BandwidthLimit"] = selectedBandwidth;
                WriteSettingsRoot(root);
            }
            catch { }
        }

        private Dictionary<string, object> ReadSettingsRoot()
        {
            try
            {
                if (!File.Exists(settingsFile)) return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                string json = File.ReadAllText(settingsFile, Encoding.UTF8);
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> parsed = js.DeserializeObject(json) as Dictionary<string, object>;
                if (parsed != null) return new Dictionary<string, object>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        private void WriteSettingsRoot(Dictionary<string, object> root)
        {
            try
            {
                Directory.CreateDirectory(settingsDir);
                JavaScriptSerializer js = new JavaScriptSerializer();
                File.WriteAllText(settingsFile, js.Serialize(root), Encoding.UTF8);
            }
            catch { }
        }

        private string LoadSettingRaw(string name)
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                object v;
                if (root.TryGetValue(name, out v) && v != null) return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\" + AppName, false))
                {
                    object v = key == null ? null : key.GetValue(name);
                    if (v != null) return v.ToString();
                }
            }
            catch { }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\" + LegacyAppName, false))
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
            string value = LoadSettingRaw(name);
            return String.IsNullOrEmpty(value) ? defaultValue : value;
        }

        private void SaveSetting(string name, string value)
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                root[name] = value ?? "";
                WriteSettingsRoot(root);
            }
            catch { }
        }

        private void DeleteSetting(string name)
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                if (root.ContainsKey(name)) root.Remove(name);
                WriteSettingsRoot(root);
            }
            catch { }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\" + AppName)) { key.DeleteValue(name, false); }
            }
            catch { }
        }

        private string NormalizeDriveLetter(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return DefaultDriveLetter;
            string v = value.Trim().ToUpperInvariant();
            if (v.Length == 1 && v[0] >= 'A' && v[0] <= 'Z') return v + ":";
            if (v.Length >= 2 && v[1] == ':' && v[0] >= 'A' && v[0] <= 'Z') return v.Substring(0, 2);
            return DefaultDriveLetter;
        }

        private string NormalizeRemoteName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return DefaultRemoteName;
            string v = value.Trim();
            return v.EndsWith(":") ? v : v + ":";
        }

        private string RemoteNameBare(string value)
        {
            string v = value ?? DefaultRemoteName;
            return v.EndsWith(":") ? v.Substring(0, v.Length - 1) : v;
        }

        private string NormalizeMountMode(string value)
        {
            return String.Equals(value, "fixed", StringComparison.OrdinalIgnoreCase) ? "fixed" : "network";
        }

        private string NormalizeProvider(string provider, string remote)
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

        private string DisplayProvider(string provider)
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

        private string ToStringValue(object value, string fallback)
        {
            if (value == null) return fallback;
            string s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            return String.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        private bool ToBool(object value)
        {
            if (value == null) return false;
            if (value is bool) return (bool)value;
            string s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            return String.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || String.Equals(s, "1", StringComparison.OrdinalIgnoreCase) || String.Equals(s, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private string SafeFileName(string value)
        {
            string s = String.IsNullOrWhiteSpace(value) ? "remote" : value;
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private string TrimForMenu(string value, int max)
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
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++) if (String.Equals(args[i], wanted, StringComparison.OrdinalIgnoreCase)) return true;
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

        private void ApplyTrayMenuTheme(ContextMenuStrip strip)
        {
            if (strip == null) return;
            try
            {
                strip.Renderer = new PixelpipeMenuRenderer();
                strip.BackColor = Color.FromArgb(14, 18, 24);
                strip.ForeColor = Color.FromArgb(230, 237, 243);
                strip.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
                strip.ShowImageMargin = false;
                strip.Padding = new Padding(8, 8, 8, 8);
            }
            catch { }
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

        private void SetCustomBandwidth()
        {
            string value = PromptForValue("Custom bandwidth limit", "Examples: 512K, 1M, 10M, 50M. Use off for unlimited.", selectedBandwidth);
            if (value == null) return;
            value = value.Trim();
            if (value.Length == 0) return;
            SetBandwidth(value);
        }

        private void OpenSettingsFile()
        {
            try
            {
                Directory.CreateDirectory(settingsDir);
                if (!File.Exists(settingsFile)) SaveProfiles();
                Process.Start("notepad.exe", Quote(settingsFile));
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void OpenLogFolder()
        {
            try { Directory.CreateDirectory(logDir); Process.Start(logDir); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private string TailLog(RemoteProfile p, int maxChars)
        {
            try
            {
                string file = p == null ? "" : p.LogFile;
                if (!File.Exists(file)) return "No log file exists yet.";
                string s = File.ReadAllText(file);
                if (s.Length <= maxChars) return s;
                return s.Substring(s.Length - maxChars);
            }
            catch (Exception ex) { return ex.Message; }
        }

        private void CheckForUpdates()
        {
            try { Process.Start("https://github.com/NathanNeurotic/Pixelpipe/releases/latest"); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe update check", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
                RebuildMenu();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe startup setting", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void ExitApp()
        {
            if (AnyMounted())
            {
                DialogResult result = MessageBox.Show("One or more Pixelpipe remotes are mounted.\r\n\r\nUnmount them before exiting?", "Pixelpipe", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Cancel) return;
                if (result == DialogResult.Yes)
                {
                    for (int i = 0; i < profiles.Count; i++) if (IsMounted(profiles[i])) UnmountProfile(profiles[i], true);
                }
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
    }

    internal sealed class PixelpipeMenuRenderer : ToolStripProfessionalRenderer
    {
        public PixelpipeMenuRenderer() : base(new PixelpipeColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled) e.TextColor = Color.FromArgb(128, 139, 150);
            else e.TextColor = Color.FromArgb(230, 237, 243);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(8, e.Item.Height / 2, e.Item.Width - 16, 1);
            using (Pen p = new Pen(Color.FromArgb(48, 54, 61))) e.Graphics.DrawLine(p, rect.Left, rect.Top, rect.Right, rect.Top);
        }
    }

    internal sealed class PixelpipeColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(14, 18, 24); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(14, 18, 24); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(14, 18, 24); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(14, 18, 24); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(31, 111, 235); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(88, 166, 255); } }
        public override Color MenuBorder { get { return Color.FromArgb(48, 54, 61); } }
        public override Color SeparatorDark { get { return Color.FromArgb(48, 54, 61); } }
        public override Color SeparatorLight { get { return Color.FromArgb(48, 54, 61); } }
    }
}
