using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pixelpipe
{
    // Win32 Job Object that any rclone child we spawn is assigned to. The job
    // is created with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; when Pixelpipe exits
    // for ANY reason (clean Exit, crash, Task Manager kill, sign-out, even
    // debugger detach) the OS closes the job handle and forcibly terminates
    // every process in the job. This is the bulletproof Windows-native way to
    // prevent orphaned rclone mounts surviving Pixelpipe's death.
    //
    // The Job Object stays alive for the lifetime of the process via a static
    // GC-rooted handle. If AssignProcessToJobObject fails (very rare — only on
    // ancient Windows with no nested-job support, or when Pixelpipe itself is
    // already in another job that disallows breakaway) we log and continue;
    // the orphan-scan path below picks up the slack on the next launch.
    internal static class RcloneJob
    {
        private static IntPtr jobHandle = IntPtr.Zero;
        private static readonly object jobLock = new object();
        private static bool initialised;

        // Job information classes (winnt.h). We only set ExtendedLimit so we
        // can flip KILL_ON_JOB_CLOSE; everything else inherits the defaults.
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public IntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInformationClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        // Called once at TrayContext construction. Idempotent; if we've
        // already set the job up, this is a no-op.
        public static void EnsureInitialised(Action<string> logWarn)
        {
            lock (jobLock)
            {
                if (initialised) return;
                initialised = true;
                try
                {
                    IntPtr h = CreateJobObject(IntPtr.Zero, null);
                    if (h == IntPtr.Zero)
                    {
                        if (logWarn != null) logWarn("CreateJobObject returned NULL; orphan-kill safety net disabled (last Win32 error " + Marshal.GetLastWin32Error() + ")");
                        return;
                    }
                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    int size = Marshal.SizeOf(info);
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(info, ptr, false);
                        if (!SetInformationJobObject(h, JobObjectExtendedLimitInformation, ptr, (uint)size))
                        {
                            if (logWarn != null) logWarn("SetInformationJobObject failed; orphan-kill safety net may not work (last Win32 error " + Marshal.GetLastWin32Error() + ")");
                            return;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                    jobHandle = h; // intentionally NOT closed; Windows keeps the job alive while the handle is open, and we want the job alive until Pixelpipe dies.
                }
                catch (Exception ex)
                {
                    if (logWarn != null) logWarn("RcloneJob init failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        // Best-effort: assign a freshly-started process to the kill-on-close
        // job. Failures are logged and ignored — the orphan-scan path covers
        // any rclone that slips through.
        public static void TryAssign(Process p, Action<string> logWarn)
        {
            if (p == null) return;
            IntPtr h;
            lock (jobLock) { h = jobHandle; }
            if (h == IntPtr.Zero) return;
            try
            {
                if (!AssignProcessToJobObject(h, p.Handle))
                {
                    if (logWarn != null) logWarn("AssignProcessToJobObject failed for pid " + p.Id + " (last Win32 error " + Marshal.GetLastWin32Error() + ")");
                }
            }
            catch (Exception ex)
            {
                if (logWarn != null) logWarn("AssignProcessToJobObject threw for pid " + (p == null ? -1 : SafePid(p)) + ": " + ex.Message);
            }
        }

        private static int SafePid(Process p) { try { return p.Id; } catch { return -1; } }
    }

    internal sealed partial class TrayContext
    {
        // Find any rclone.exe processes whose command line mentions one of our
        // profiles' drive letters. Called once at startup; processes we own
        // this session won't have been launched yet so anything matching has
        // to be an orphan from a previous Pixelpipe that didn't clean up.
        //
        // Why we keep this even with the Job Object: users upgrading from a
        // pre-v0.11.4 Pixelpipe still have leftover rclone processes that
        // were never assigned to a job. Without this scan they'd be stuck
        // with "drive in use" until they figured out Task Manager themselves.
        private List<OrphanRclone> FindOrphanRcloneProcesses()
        {
            List<OrphanRclone> result = new List<OrphanRclone>();
            Process[] procs;
            try { procs = Process.GetProcessesByName("rclone"); }
            catch { return result; }

            HashSet<string> ourDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < snapshot.Length; i++)
            {
                string d = NormalizeDriveLetter(snapshot[i].DriveLetter);
                if (!String.IsNullOrEmpty(d)) ourDrives.Add(d);
            }

            for (int i = 0; i < procs.Length; i++)
            {
                Process pr = procs[i];
                try
                {
                    string cmdline = GetProcessCommandLine(pr.Id);
                    if (String.IsNullOrEmpty(cmdline)) continue;
                    // rclone mount arg is the drive letter as-is, e.g. P:
                    // Match it surrounded by whitespace or quotes so we don't
                    // false-positive on e.g. "Pixeldrain:" remote-name colons.
                    foreach (string d in ourDrives)
                    {
                        if (CommandLineMentionsDrive(cmdline, d))
                        {
                            result.Add(new OrphanRclone { Process = pr, DriveLetter = d, CommandLine = cmdline });
                            break;
                        }
                    }
                }
                catch (Exception ex) { LogUiIssue("orphan scan pid " + SafePid(pr), ex); }
            }
            return result;
        }

        // Pure helper, exposed for tests: returns true iff cmdline includes
        // the bare drive letter as a separate argument (whitespace or quote
        // boundary on each side). Avoids false matching on remote-name
        // colons or partial drive letters.
        internal static bool CommandLineMentionsDrive(string commandLine, string driveLetter)
        {
            if (String.IsNullOrEmpty(commandLine) || String.IsNullOrEmpty(driveLetter)) return false;
            string d = driveLetter.Trim().TrimEnd('\\');
            if (d.Length < 2) return false;
            // Patterns we accept: ` P:`, `"P:"`, `P:\`, `"P:\"` (with quotes
            // closing the trailing slash). Mount arg is normalised by
            // QuoteArg so it's always inside double quotes.
            string[] needles = new string[]
            {
                " " + d + " ",
                "\"" + d + "\"",
                " " + d + "\\",
                "\"" + d + "\\",
            };
            for (int i = 0; i < needles.Length; i++)
            {
                if (commandLine.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // Returns the full command line for a running process via WMI.
        // Slower than NtQueryInformationProcess but stable across Windows
        // versions and doesn't require undocumented APIs.
        private string GetProcessCommandLine(int processId)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId.ToString()))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        try { return (mo["CommandLine"] as string) ?? ""; } finally { mo.Dispose(); }
                    }
                }
            }
            catch (Exception ex) { LogUiIssue("wmi cmdline pid " + processId, ex); }
            return "";
        }

        // Tray-menu / startup hook. Surfaces a Yes/No prompt summarising the
        // orphans found and, on Yes, kills each one. Used by:
        //  - StartupOrphanCheck (silent if none, prompt if found)
        //  - The "Kill orphan rclone for P:" button in the new "drive in use"
        //    dialog (KillOrphansForDrive below)
        private bool PromptAndKillOrphans(List<OrphanRclone> orphans, bool silentIfNone)
        {
            if (orphans == null || orphans.Count == 0)
            {
                if (!silentIfNone) ShowBalloon("No orphan rclone processes found.");
                return false;
            }
            StringBuilder msg = new StringBuilder();
            msg.AppendLine("Pixelpipe found " + orphans.Count + " orphan rclone process" + (orphans.Count == 1 ? "" : "es") + " from a previous session.");
            msg.AppendLine();
            int show = Math.Min(orphans.Count, 4);
            for (int i = 0; i < show; i++) msg.AppendLine("  pid " + SafePid(orphans[i].Process) + " on " + orphans[i].DriveLetter);
            if (orphans.Count > show) msg.AppendLine("  ... and " + (orphans.Count - show) + " more");
            msg.AppendLine();
            msg.AppendLine("Kill them so Pixelpipe can take over the drive letter(s)?");
            DialogResult r = MessageBox.Show(msg.ToString(), "Pixelpipe", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return false;
            int killed = 0;
            for (int i = 0; i < orphans.Count; i++)
            {
                try
                {
                    orphans[i].Process.Kill();
                    orphans[i].Process.WaitForExit(3000);
                    killed++;
                    LogUiWarn("orphan kill", "killed pid " + SafePid(orphans[i].Process) + " holding " + orphans[i].DriveLetter);
                }
                catch (Exception ex) { LogUiIssue("orphan kill pid " + SafePid(orphans[i].Process), ex); }
            }
            // Windows may take a moment to release the drive letter after the
            // process dies; brief sleep so the next mount attempt doesn't
            // race the kernel's mount-point cleanup.
            Thread.Sleep(800);
            ShowBalloon("Killed " + killed + " orphan rclone process" + (killed == 1 ? "" : "es") + ".");
            return killed > 0;
        }

        // Helper for the "drive in use" dialog: find and kill any orphan
        // rclone holding the given drive letter. Returns true if at least
        // one orphan was killed (so the caller can retry the mount).
        private bool KillOrphansForDrive(string driveLetter)
        {
            string d = NormalizeDriveLetter(driveLetter);
            List<OrphanRclone> all = FindOrphanRcloneProcesses();
            List<OrphanRclone> matching = new List<OrphanRclone>();
            for (int i = 0; i < all.Count; i++)
            {
                if (String.Equals(all[i].DriveLetter, d, StringComparison.OrdinalIgnoreCase)) matching.Add(all[i]);
            }
            if (matching.Count == 0)
            {
                MessageBox.Show("No orphan rclone process found for " + d + ". The drive letter may be held by another app (TeraBox, Dropbox, mapped network drive, …).", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            int killed = 0;
            for (int i = 0; i < matching.Count; i++)
            {
                try
                {
                    matching[i].Process.Kill();
                    matching[i].Process.WaitForExit(3000);
                    killed++;
                    LogUiWarn("orphan kill", "killed pid " + SafePid(matching[i].Process) + " holding " + d);
                }
                catch (Exception ex) { LogUiIssue("orphan kill drive " + d, ex); }
            }
            Thread.Sleep(800);
            return killed > 0;
        }

        // Called from the TrayContext constructor after profiles are loaded.
        // Runs the scan on a worker thread so it doesn't block UI startup.
        // Posts the prompt back via BeginUi if anything is found.
        private void StartupOrphanCheck()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // Tiny stagger so the tray icon and menu paint first.
                    Thread.Sleep(700);
                    List<OrphanRclone> orphans = FindOrphanRcloneProcesses();
                    if (orphans.Count == 0) return;
                    BeginUi(delegate { PromptAndKillOrphans(orphans, true); });
                }
                catch (Exception ex) { LogUiIssue("startup orphan check", ex); }
            });
        }

        private static int SafePid(Process p) { try { return p == null ? -1 : p.Id; } catch { return -1; } }

        internal sealed class OrphanRclone
        {
            public Process Process;
            public string DriveLetter;
            public string CommandLine;
        }
    }
}
