using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext : ApplicationContext
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
        private readonly object profilesLock = new object();
        private readonly List<ToolStripMenuItem> bandwidthItems;
        private string selectedBandwidth;
        private volatile string rclonePath;
        private string settingsDir;
        private string settingsFile;
        private string logDir;
        private string uiLogFile;
        private string setupStatusText;
        private string transferQuotaText;
        private int refreshingFlag;
        private int dependencyRefreshingFlag;
        private DateTime lastDependencyRefreshUtc = DateTime.MinValue;
        private DateTime lastQuotaRefreshUtc = DateTime.MinValue;
        private string[] cachedRcloneRemotes;
        private DateTime lastRemoteListUtc = DateTime.MinValue;
        private bool adminWarningShown;
        private bool verboseLogging;
        private bool rebuildPendingWhileOpen;

        public TrayContext(string[] args)
        {
            settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            settingsFile = Path.Combine(settingsDir, "settings.json");
            logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "logs");
            uiLogFile = Path.Combine(logDir, "pixelpipe-ui.log");
            Directory.CreateDirectory(settingsDir);
            Directory.CreateDirectory(logDir);

            rclonePath = FindRclonePath();
            selectedBandwidth = NormalizeBandwidthLimit(LoadSetting("BandwidthLimit", "off"));
            transferQuotaText = ApiKeyConfigured() ? "Transfer quota: not checked" : "Transfer quota: PixelDrain API key not set";
            setupStatusText = "Setup: not checked";
            verboseLogging = String.Equals(LoadSetting("VerboseLogging", "0"), "1", StringComparison.OrdinalIgnoreCase);
            // If the previous run recorded an available update, surface it again so
            // the tray menu item appears without waiting another 24 hours.
            availableUpdateVersion = LoadSetting("AvailableUpdateVersion", "");
            profiles = LoadProfiles();
            AssignRuntimeFields();

            bandwidthItems = new List<ToolStripMenuItem>();
            menu = new ContextMenuStrip();
            ApplyTrayMenuTheme(menu);
            menu.Opening += delegate { OnMenuOpening(); };
            // If a refresh wants to rebuild the menu while it's visible, defer it
            // until close so the menu doesn't flash in the user's face.
            menu.Closed += delegate
            {
                if (rebuildPendingWhileOpen)
                {
                    rebuildPendingWhileOpen = false;
                    RebuildMenu();
                }
            };

            tray = new NotifyIcon();
            tray.Icon = LoadAppIcon();
            tray.Text = "Pixelpipe";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { TogglePrimaryProfile(); };

            // Every constructor step from here down is wrapped so that a
            // failure (FileSystemWatcher init, schedule timer creation, even a
            // corrupt profile in the refresh loop) cannot prevent RebuildMenu
            // from running. Before v0.11.1, an unhandled exception in
            // StartWatchFolders bypassed RebuildMenu and left the tray with a
            // visible icon but an empty context menu, which read as "the app
            // is dead" even when the rest of the process was healthy.
            try
            {
                timer = new System.Windows.Forms.Timer();
                timer.Interval = 7000;
                timer.Tick += delegate { MonitorMountHealth(); QueueRefresh(false, false); };
                timer.Start();
            }
            catch (Exception ex) { LogUiIssue("startup timer", ex); }

            // Separate 30-second timer for per-profile mount/unmount schedules.
            try { StartScheduleTimer(); } catch (Exception ex) { LogUiIssue("startup schedule timer", ex); }
            // Watch-folder uploader (per-profile FileSystemWatcher + a single
            // 3-second drain timer that hands off ready files to a worker).
            try { StartWatchFolders(); } catch (Exception ex) { LogUiIssue("startup watch folders", ex); }

            try { RebuildMenu(); }
            catch (Exception ex)
            {
                LogUiIssue("startup rebuild menu", ex);
                try { BuildEmergencyMenu(ex); } catch { }
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Thread.Sleep(900);
                    BeginUi(delegate { FirstLaunchSetupIfNeeded(); RefreshDependencyStatusAsync(true); });
                }
                catch (Exception ex) { LogUiIssue("first-launch background", ex); }
            });

            if (Program.HasArg(args, "/automount"))
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Thread.Sleep(5000);
                        BeginUi(delegate { MountAutoProfiles(); });
                    }
                    catch (Exception ex) { LogUiIssue("automount background", ex); }
                });
            }
            else if (!String.Equals(LoadSetting("WelcomeBalloonShown", "0"), "1", StringComparison.OrdinalIgnoreCase))
            {
                // Once per install, remind the user where the app lives. If the user
                // closes the main window thinking it quit Pixelpipe, the tray icon is
                // still there and this balloon makes that clear.
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Thread.Sleep(2000);
                        BeginUi(delegate
                        {
                            ShowBalloon("Pixelpipe is in your system tray. Right-click the icon for the menu, or use Exit there to fully quit.");
                            SaveSetting("WelcomeBalloonShown", "1");
                        });
                    }
                    catch (Exception ex) { LogUiIssue("welcome balloon", ex); }
                });
            }
        }

        private void AssignRuntimeFields()
        {
            lock (profilesLock)
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
        }

        private int ProfilePort(RemoteProfile p)
        {
            string id = p == null || String.IsNullOrWhiteSpace(p.Id) ? Guid.NewGuid().ToString("N") : p.Id;
            return ProfilePortFor(id);
        }

        // Deterministic port mapping for a stable profile id. Exposed for unit tests.
        internal static int ProfilePortFor(string id)
        {
            if (String.IsNullOrEmpty(id)) id = "";
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
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < snapshot.Length; i++) if (IsMounted(snapshot[i])) mounted++;
            if (mounted == 0) return "no remotes mounted";
            return mounted.ToString() + " of " + snapshot.Length.ToString() + " remotes mounted";
        }

        private Icon LoadAppIcon()
        {
            string exeIcon = Application.ExecutablePath;
            try
            {
                Icon extracted = Icon.ExtractAssociatedIcon(exeIcon);
                if (extracted != null) return extracted;
            }
            catch (Exception ex) { LogUiIssue("load app icon", ex); }

            // Fallback: synthesize an icon. Clone owns its own handle so the original
            // HICON returned by GetHicon can be freed without invalidating the result.
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        using (Brush b = new SolidBrush(Color.FromArgb(34, 120, 210))) g.FillEllipse(b, 1, 1, 30, 30);
                        using (Pen p = new Pen(Color.White, 2)) g.DrawEllipse(p, 2, 2, 28, 28);
                        using (Font f = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (Brush w = new SolidBrush(Color.White)) g.DrawString("P", f, w, 9, 5);
                    }
                    hIcon = bmp.GetHicon();
                    using (Icon source = Icon.FromHandle(hIcon))
                    {
                        return (Icon)source.Clone();
                    }
                }
            }
            finally
            {
                if (hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(hIcon);
            }
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

        private void LogUiIssue(string area, Exception ex)
        {
            WriteLogLine("error", area, ex == null ? "(null exception)" : ex.GetType().Name + ": " + ex.Message);
        }

        private void LogUiInfo(string area, string message)
        {
            WriteLogLine("info", area, message);
        }

        private void LogUiWarn(string area, string message)
        {
            WriteLogLine("warn", area, message);
        }

        private void WriteLogLine(string level, string area, string message)
        {
            try
            {
                Directory.CreateDirectory(logDir);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "]" +
                              (String.IsNullOrEmpty(area) ? "" : " [" + area + "]") +
                              " " + message + Environment.NewLine;
                File.AppendAllText(uiLogFile, line);
            }
            catch { }
        }

        // Snapshot the profile list under the lock. UI thread mutates the list; worker
        // threads must iterate over a snapshot to avoid Collection-modified exceptions.
        private RemoteProfile[] SnapshotProfiles()
        {
            lock (profilesLock) return profiles.ToArray();
        }

        // Best-effort scrubbing of secrets before showing rclone output to the user.
        // Hides api_key/password/token assignments and long alphanumeric runs that
        // look like credentials.
        internal static string ScrubSecrets(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            string s = Regex.Replace(text, @"(?i)(api_key|password|token|secret|access_key|secret_key)\s*[=:]\s*\S+", "$1=***");
            s = Regex.Replace(s, @"\b[A-Za-z0-9_\-]{32,}\b", "***");
            return s;
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
            catch (Exception ex) { LogUiIssue("ui dispatch", ex); }
        }

        private void ShowBalloon(string message)
        {
            try
            {
                tray.BalloonTipTitle = "Pixelpipe";
                tray.BalloonTipText = message;
                tray.ShowBalloonTip(1800);
            }
            catch (Exception ex) { LogUiIssue("tray balloon", ex); }
        }

        private void ExitApp()
        {
            if (AnyMounted())
            {
                DialogResult result = MessageBox.Show("One or more Pixelpipe remotes are mounted.\r\n\r\nUnmount them before exiting?", "Pixelpipe", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Cancel) return;
                if (result == DialogResult.Yes)
                {
                    RemoteProfile[] snapshot = SnapshotProfiles();
                    for (int i = 0; i < snapshot.Length; i++) if (IsMounted(snapshot[i])) UnmountProfile(snapshot[i], true);
                }
            }
            tray.Visible = false;
            tray.Dispose();
            Application.Exit();
        }
    }
}
