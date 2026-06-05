using System;
using System.Diagnostics;
using System.IO;

namespace Pixelpipe
{
    // ARCH-1 step 4 (v0.15.4, audit): fourth and final collaborator
    // extracted from the TrayContext partial. Owns just the rclone mount
    // process-lifecycle bits: argument assembly + Process.Start + Job Object
    // binding. The dialog flow (rclone-missing, WinFsp-missing, remote-not-
    // configured, drive-in-use, post-launch result) stays in TrayContext
    // because it's heavily UI-coupled.
    //
    // This is intentionally a thin extraction. The audit calls ARCH-1
    // incremental; later releases can pull more orchestration in once the
    // shape of "what doesn't need a TrayContext" stabilises.
    internal sealed class MountManager
    {
        // Pure helper, tests cover it. Builds the rclone mount argv that
        // Pixelpipe has used since v0.5.x with all the cache-mode, VFS,
        // network-mode, RC, and bandwidth flags in one place. Caller passes
        // RcCommonFlags + effective bandwidth so this class doesn't need to
        // know about the per-profile bandwidth resolution. RC credentials are
        // passed via environment variables, not argv.
        internal static string BuildMountArgs(RemoteProfile p, bool fullCache, string rcCommonFlags, string effectiveBandwidth)
        {
            if (p == null) return "";
            string cacheMode = fullCache ? "full" : "writes";
            string args = "mount " + TrayContext.QuoteArg(TrayContext.NormalizeRemoteName(p.Remote))
                + " " + TrayContext.QuoteArg(TrayContext.NormalizeDriveLetter(p.DriveLetter))
                + " --links"
                + (String.Equals(p.MountMode, "network", StringComparison.OrdinalIgnoreCase) ? " --network-mode" : "")
                + " --vfs-cache-mode " + cacheMode
                + " --dir-cache-time 10m"
                + " --poll-interval 1m"
                + " --vfs-write-back 10s"
                + " --vfs-cache-max-age 6h"
                + " --vfs-cache-max-size 5G"
                + " --volname " + TrayContext.QuoteArg(p.Label)
                + " --rc " + (rcCommonFlags ?? "")
                + " --log-level INFO"
                + " --log-file " + TrayContext.QuoteArg(p.LogFile);
            if (!String.IsNullOrEmpty(effectiveBandwidth)
                && !String.Equals(effectiveBandwidth, "off", StringComparison.OrdinalIgnoreCase))
            {
                args += " --bwlimit " + effectiveBandwidth;
            }
            return args;
        }

        // Spawns rclone mount and binds the child to the kill-on-job-close
        // Job Object so it dies with Pixelpipe even on Task Manager kill /
        // crash / sign-out. Returns the started Process; caller is
        // responsible for the post-launch monitor (1900 ms wait + exit
        // check + UI feedback).
        internal static Process StartMountProcess(string rclonePath, string args, System.Collections.Generic.Dictionary<string, string> envOverrides, Action<string> logJobWarn)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = rclonePath;
            psi.Arguments = args;
            if (envOverrides != null)
            {
                foreach (System.Collections.Generic.KeyValuePair<string, string> kv in envOverrides)
                {
                    if (String.IsNullOrEmpty(kv.Key)) continue;
                    psi.EnvironmentVariables[kv.Key] = kv.Value ?? "";
                }
            }
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Process child = Process.Start(psi);
            RcloneJob.TryAssign(child, logJobWarn);
            return child;
        }
    }
}
