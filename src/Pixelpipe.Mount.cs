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
            RemoteProfile[] snapshot = SnapshotProfiles();
            int mounted = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].AutoMount) { MountProfile(snapshot[i], snapshot[i].FullCache); mounted++; }
            }
            if (mounted == 0)
            {
                LogUiInfo("automount", "no profiles tagged auto-mount; nothing started");
                ShowBalloon("Pixelpipe started; no profiles are tagged for auto-mount.");
            }
            else
            {
                ShowBalloon("Pixelpipe auto-mounted " + mounted.ToString() + " profile(s).");
            }
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
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < snapshot.Length; i++) if (IsMounted(snapshot[i])) return true;
            return false;
        }

        private int CountMounted()
        {
            int n = 0;
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < snapshot.Length; i++) if (IsMounted(snapshot[i])) n++;
            return n;
        }

        private bool IsMounted(RemoteProfile p)
        {
            if (p == null || p.MountProcess == null) return false;
            try { return !p.MountProcess.HasExited; } catch { return false; }
        }

        // Entry point — UI-thread guard. The expensive checks (rclone path
        // probe, listremotes for remote config, drive enumeration) move to
        // a worker thread so the menu click doesn't freeze the UI for the
        // few seconds those take. Dialogs and the actual process spawn
        // happen back on the UI thread once the worker reports.
        private void MountProfile(RemoteProfile p, bool fullCache)
        {
            if (p == null) return;
            if (IsMounted(p))
            {
                ShowBalloon(p.Label + " is already mounted.");
                return;
            }
            ShowBalloon(p.Label + ": preparing mount...");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    rclonePath = FindRclonePath();
                    bool rclonePresent = RcloneAvailable();
                    bool winfspPresent = WinFspInstalled();
                    bool remoteOk = rclonePresent ? RemoteConfigured(p) : false;
                    bool driveTaken = DriveLetterInUse(p.DriveLetter);
                    BeginUi(delegate
                    {
                        try { ContinueMountOnUiThread(p, fullCache, rclonePresent, winfspPresent, remoteOk, driveTaken); }
                        catch (Exception ex) { LogUiIssue("continue mount " + p.Label, ex); }
                    });
                }
                catch (Exception ex)
                {
                    LogUiIssue("mount precheck " + p.Label, ex);
                    BeginUi(delegate { ShowBalloon(p.Label + ": mount preparation failed: " + ex.Message); });
                }
            });
        }

        private void ContinueMountOnUiThread(RemoteProfile p, bool fullCache, bool rclonePresent, bool winfspPresent, bool remoteOk, bool driveTaken)
        {
            if (!rclonePresent)
            {
                DialogResult r = MessageBox.Show("rclone.exe was not found.\r\n\r\nDownload the portable Windows rclone build into your user profile now?", "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) DownloadRclonePortableWithUi();
                if (!RcloneAvailable())
                {
                    MessageBox.Show("rclone is still unavailable. Use Setup / dependencies from the tray menu, or install rclone manually.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                rclonePresent = true;
            }

            if (!winfspPresent)
            {
                DialogResult r = MessageBox.Show("WinFsp is required for rclone mount on Windows and does not appear to be installed.\r\n\r\nInstall WinFsp with winget now?", "Pixelpipe setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) InstallWinFspWithWinget();
                MessageBox.Show("After WinFsp finishes installing, mount again from the tray menu. A Windows restart may be required on some systems.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!remoteOk)
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
                // Re-check remote config (this is a synchronous rclone
                // listremotes — small price, only on the failure path).
                if (!RemoteConfigured(p))
                {
                    MessageBox.Show("The selected rclone remote is still not configured. The mount cannot start until rclone has this remote.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (driveTaken)
            {
                string prompt = p.DriveLetter + " appears to already be in use.\r\n\r\n"
                              + "Yes  — find and kill an orphan rclone process for " + p.DriveLetter + " (most common cause), then mount.\r\n"
                              + "No   — try to mount anyway (rarely works; rclone will probably exit immediately).\r\n"
                              + "Cancel — leave it alone.";
                DialogResult r = MessageBox.Show(prompt, "Pixelpipe", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes)
                {
                    bool killed = KillOrphansForDrive(p.DriveLetter);
                    if (!killed) return;
                    if (DriveLetterInUse(p.DriveLetter))
                    {
                        MessageBox.Show(p.DriveLetter + " is still in use after killing the orphan rclone. Something else is holding it — close any File Explorer windows on " + p.DriveLetter + " and try again.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }

            if (IsAdministrator() && !adminWarningShown)
            {
                MessageBox.Show("This app is currently running as Administrator.\r\n\r\nThe mount may work, but the drive can be hidden from normal File Explorer. Exit and run the app normally unless you specifically need an elevated mount.\r\n\r\nThis warning won't show again until you restart Pixelpipe.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                adminWarningShown = true;
            }

            Directory.CreateDirectory(logDir);
            p.FullCache = fullCache;
            p.DesiredMounted = true;
            p.LastError = "";
            string cacheMode = fullCache ? "full" : "writes";
            string args = "mount " + QuoteArg(NormalizeRemoteName(p.Remote)) + " " + QuoteArg(NormalizeDriveLetter(p.DriveLetter)) +
                          " --links" +
                          (String.Equals(p.MountMode, "network", StringComparison.OrdinalIgnoreCase) ? " --network-mode" : "") +
                          " --vfs-cache-mode " + cacheMode +
                          " --dir-cache-time 10m" +
                          " --poll-interval 1m" +
                          " --vfs-write-back 10s" +
                          " --vfs-cache-max-age 6h" +
                          " --vfs-cache-max-size 5G" +
                          " --volname " + QuoteArg(p.Label) +
                          " --rc " + RcCommonFlags(p.RcPort) +
                          " --log-level INFO" +
                          " --log-file " + QuoteArg(p.LogFile);

            string effectiveBandwidth = EffectiveBandwidthFor(p);
            if (!String.Equals(effectiveBandwidth, "off", StringComparison.OrdinalIgnoreCase)) args += " --bwlimit " + effectiveBandwidth;

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
                // Bind the new rclone to our kill-on-job-close Job Object so
                // it dies with Pixelpipe even if Pixelpipe is killed via Task
                // Manager / crashes / etc. Best-effort; the orphan-scan path
                // catches anything that slips through.
                RcloneJob.TryAssign(p.MountProcess, delegate(string warn) { LogUiWarn("rclone job assign " + p.Label, warn); });
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
                                    MessageBox.Show("rclone exited immediately.\r\n\r\nMost likely causes:\r\n- WinFsp is missing\r\n- selected drive letter is already in use\r\n- selected rclone remote is not configured\r\n- rclone is being forced to run elevated\r\n\r\nLog tail:\r\n" + ScrubSecrets(tail), "Pixelpipe mount error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                                else
                                {
                                    p.StatusText = "mounted on " + GetDriveRoot(p) + (fullCache ? " - full cache" : " - low overhead");
                                    ShowBalloon(p.Label + " mounted on " + GetDriveRoot(p));
                                    QueueRefresh(true, true);
                                    RebuildMenu();
                                }
                            }
                            catch (Exception ex) { LogUiIssue("mount post-launch check", ex); }
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
            p.DesiredMounted = false;
            if (!IsMounted(p))
            {
                FinalizeUnmounted(p, silent);
                return;
            }
            p.StatusText = "unmounting";
            RebuildMenu();
            ThreadPool.QueueUserWorkItem(delegate { UnmountWorker(p, silent); });
        }

        private void UnmountWorker(RemoteProfile p, bool silent)
        {
            string unmountResult = "";
            string quitResult = "";
            try
            {
                unmountResult = RunRcloneCapture("rc mount/unmount " + QuoteArg("mountPoint=" + NormalizeDriveLetter(p.DriveLetter)) + " " + RcCommonFlags(p.RcPort), 2500);
                Thread.Sleep(800);
                if (IsMounted(p)) quitResult = RunRcloneCapture("rc core/quit " + RcCommonFlags(p.RcPort), 2500);
                Thread.Sleep(1200);
            }
            catch (Exception ex) { LogUiIssue("unmount worker", ex); }

            BeginUi(delegate
            {
                if (IsMounted(p))
                {
                    p.LastError = BuildUnmountFallbackText(p, unmountResult, quitResult);
                    LogUiWarn("unmount fallback", p.LastError);

                    if (silent || PromptForceKillUnmount(p) == DialogResult.Yes)
                    {
                        if (!TryKillMountProcess(p))
                        {
                            p.StatusText = "unmount failed";
                            RebuildMenu();
                            if (!silent) MessageBox.Show("Pixelpipe could not stop the rclone process. See pixelpipe-ui.log for details.", "Pixelpipe unmount", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                FinalizeUnmounted(p, silent);
            });
        }

        private void FinalizeUnmounted(RemoteProfile p, bool silent)
        {
            CleanStaleDriveMappings(p, false);
            p.StatusText = "not mounted";
            p.SpeedText = "speed not mounted";
            p.SessionText = "session not mounted";
            DisposeProcess(p);
            p.MountProcess = null;
            RebuildMenu();
            if (!silent) ShowBalloon(p.Label + " unmounted.");
        }

        private static void DisposeProcess(RemoteProfile p)
        {
            try
            {
                if (p != null && p.MountProcess != null) p.MountProcess.Dispose();
            }
            catch { }
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
                if (!p.MountProcess.HasExited)
                {
                    // Use taskkill /F /T to also terminate any child process (the rclone
                    // mount can spawn a WinFsp helper child that holds the drive even
                    // after the parent dies). .NET Framework 4.x has no entire-process-tree
                    // overload, so we shell out.
                    int pid = p.MountProcess.Id;
                    RunProcessCapture("taskkill.exe", "/F /T /PID " + pid.ToString(), 5000);
                    p.MountProcess.WaitForExit(2500);
                    if (!p.MountProcess.HasExited)
                    {
                        // Last-ditch: in-process Kill in case taskkill couldn't find it.
                        try { p.MountProcess.Kill(); } catch { }
                        p.MountProcess.WaitForExit(1000);
                    }
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
            selectedBandwidth = NormalizeBandwidthLimit(value);
            SaveSetting("BandwidthLimit", selectedBandwidth);
            RebuildMenu();

            bool any = false;
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < snapshot.Length; i++)
            {
                RemoteProfile p = snapshot[i];
                if (!IsMounted(p)) continue;
                // Only profiles that inherit the global limit get the new value.
                // Profiles with their own BandwidthLimit are left alone — that's the
                // whole point of the per-profile override.
                if (!String.IsNullOrEmpty(p.BandwidthLimit)) continue;
                any = true;
                ApplyBandwidthToMounted(p, selectedBandwidth);
            }
            ShowBalloon(any ? "Global bandwidth applied: " + DisplayLimit(selectedBandwidth) + " (profiles with their own limit kept theirs)"
                            : "Global bandwidth saved for next mount: " + DisplayLimit(selectedBandwidth));
        }

        // Resolves the limit a profile should mount with: per-profile override
        // when set and valid, otherwise the global limit. Used by both the mount
        // launch path and the live RC bwlimit push.
        private string EffectiveBandwidthFor(RemoteProfile p)
        {
            if (p == null) return selectedBandwidth;
            string per = p.BandwidthLimit;
            if (!String.IsNullOrWhiteSpace(per) && IsValidBandwidth(per.Trim())) return per.Trim();
            return selectedBandwidth;
        }

        // Per-profile bandwidth setter. Empty/null => clear the override and
        // immediately switch the mounted profile back to the global limit.
        private void SetProfileBandwidth(RemoteProfile p, string value)
        {
            if (p == null) return;
            string normalized = String.IsNullOrWhiteSpace(value) ? "" : (IsValidBandwidth(value.Trim()) ? value.Trim() : "");
            p.BandwidthLimit = normalized;
            SaveProfiles();
            if (IsMounted(p)) ApplyBandwidthToMounted(p, EffectiveBandwidthFor(p));
            RebuildMenu();
            UpdateMainWindowLiveState();
            ShowBalloon(p.Label + ": bandwidth " + (String.IsNullOrEmpty(normalized) ? "inheriting global (" + DisplayLimit(selectedBandwidth) + ")" : "set to " + DisplayLimit(normalized)));
        }

        private void ApplyBandwidthToMounted(RemoteProfile p, string rate)
        {
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    RemoteProfile profile = (RemoteProfile)state;
                    string result = RunRcloneCapture("rc core/bwlimit rate=" + rate + " " + RcCommonFlags(profile.RcPort), 4000);
                    if (result.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0 || result.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) profile.LastError = result;
                }
                catch (Exception ex) { LogUiIssue("apply bandwidth", ex); }
            }, p);
        }

        internal static bool IsValidBandwidth(string value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(value, "^(off|[0-9]+(\\.[0-9]+)?[KMG]?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private void SetCustomBandwidth()
        {
            string value = PromptForValue("Custom bandwidth limit", "Examples: 512K, 1M, 10M, 50M. Use off for unlimited.", selectedBandwidth);
            if (value == null) return;
            value = value.Trim();
            if (value.Length == 0) return;
            if (!IsValidBandwidth(value))
            {
                MessageBox.Show("Bandwidth must look like 512K, 1M, 10M, 1.5G, or 'off'.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetBandwidth(value);
        }

        private void MonitorMountHealth()
        {
            try
            {
                RemoteProfile[] snapshot = SnapshotProfiles();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    RemoteProfile p = snapshot[i];
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
            catch (Exception ex) { LogUiIssue("mount health monitor", ex); }
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
