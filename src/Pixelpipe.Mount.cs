using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
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
                    string unmountResult = RunRcloneCapture("rc mount/unmount mountPoint=" + p.DriveLetter + " --rc-addr 127.0.0.1:" + p.RcPort.ToString() + " --rc-no-auth", 2500);
                    Thread.Sleep(800);
                    string quitResult = "";
                    if (IsMounted(p)) quitResult = RunRcloneCapture("rc core/quit --rc-addr 127.0.0.1:" + p.RcPort.ToString() + " --rc-no-auth", 2500);
                    Thread.Sleep(1200);
                    if (IsMounted(p))
                    {
                        p.LastError = BuildUnmountFallbackText(p, unmountResult, quitResult);
                        LogUiIssue("unmount fallback", new InvalidOperationException(p.LastError));

                        if (silent || PromptForceKillUnmount(p) == DialogResult.Yes)
                        {
                            if (!TryKillMountProcess(p))
                            {
                                p.StatusText = "unmount failed";
                                RebuildMenu();
                                if (!silent) MessageBox.Show("Pixelpipe could not stop the rclone process.\r\n\r\n" + p.LastError, "Pixelpipe unmount", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                return;
                            }
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

        private DialogResult PromptForceKillUnmount(RemoteProfile p)
        {
            string message = p.Label + " (" + GetDriveRoot(p) + ") did not exit after Pixelpipe asked rclone to unmount cleanly.\r\n\r\nYes = stop only the rclone process Pixelpipe started for this profile.\r\nNo = leave it running and keep the status pending.";
            return MessageBox.Show(message, "Pixelpipe unmount", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        }

        private bool TryKillMountProcess(RemoteProfile p)
        {
            try
            {
                if (p.MountProcess == null) return !IsMounted(p);
                if (p.MountProcess != null && !p.MountProcess.HasExited)
                {
                    p.MountProcess.Kill();
                    p.MountProcess.WaitForExit(2500);
                }
                return p.MountProcess.HasExited;
            }
            catch (Exception ex)
            {
                p.LastError = "Force-kill failed: " + ex.Message;
                LogUiIssue("unmount force-kill", ex);
                return false;
            }
        }

        private string BuildUnmountFallbackText(RemoteProfile p, string unmountResult, string quitResult)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Clean unmount did not finish for ");
            sb.Append(p.Label);
            sb.Append(" on ");
            sb.Append(GetDriveRoot(p));
            AppendCommandResult(sb, "mount/unmount", unmountResult);
            AppendCommandResult(sb, "core/quit", quitResult);
            return sb.ToString();
        }

        private void AppendCommandResult(StringBuilder sb, string label, string result)
        {
            if (String.IsNullOrWhiteSpace(result)) return;
            string oneLine = result.Replace("\r", " ").Replace("\n", " ").Trim();
            sb.Append("; ");
            sb.Append(label);
            sb.Append(": ");
            sb.Append(TrimForMenu(oneLine, 240));
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

        private void SetCustomBandwidth()
        {
            string value = PromptForValue("Custom bandwidth limit", "Examples: 512K, 1M, 10M, 50M. Use off for unlimited.", selectedBandwidth);
            if (value == null) return;
            value = value.Trim();
            if (value.Length == 0) return;
            SetBandwidth(value);
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
    }
}
