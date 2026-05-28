using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        private void TestProfile(RemoteProfile p)
        {
            if (p == null) return;
            ShowBalloon("Testing " + p.Label + "...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string report = BuildProfilePreflightText(p);
                bool failed = PreflightHasFailures(report);
                BeginUi(delegate
                {
                    if (failed)
                    {
                        p.LastError = "Preflight found an issue. Open Test profile for details.";
                        LogUiWarn("preflight " + p.Label, report);
                    }
                    else
                    {
                        p.LastError = "";
                        LogUiInfo("preflight " + p.Label, "passed");
                    }
                    RebuildMenu();
                    UpdateMainWindowLiveState();
                    MessageBox.Show(report, p.Label + " preflight", MessageBoxButtons.OK, failed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                });
            });
        }

        private string BuildProfilePreflightText(RemoteProfile p)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Pixelpipe profile preflight");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Profile: " + (p == null ? "(none)" : p.Label));
            sb.AppendLine();

            if (p == null)
            {
                AppendPreflightLine(sb, "FAIL", "profile", "No profile was selected.");
                return sb.ToString();
            }

            bool mounted = IsMounted(p);
            string remote = NormalizeRemoteName(p.Remote);
            string drive = NormalizeDriveLetter(p.DriveLetter);

            AppendPreflightLine(sb, "OK", "provider", DisplayProvider(p.Provider));
            AppendPreflightLine(sb, "OK", "remote", remote);
            AppendPreflightLine(sb, "OK", "drive", drive);
            AppendPreflightLine(sb, "OK", "mount mode", NormalizeMountMode(p.MountMode));

            bool rclone = RcloneAvailable();
            AppendPreflightLine(sb, rclone ? "OK" : "FAIL", "rclone", rclone ? "found at " + rclonePath : "not found");
            if (rclone)
            {
                string version = FirstNonEmptyLine(RunRcloneCapture("version", 5000));
                if (version.Length > 0) AppendPreflightLine(sb, "OK", "rclone version", version);
                else AppendPreflightLine(sb, "WARN", "rclone version", "rclone did not return version text within the timeout");
            }

            bool winfsp = WinFspInstalled();
            AppendPreflightLine(sb, winfsp ? "OK" : "FAIL", "WinFsp", winfsp ? "installed" : "not detected");

            bool remoteConfigured = rclone && RemoteConfigured(p);
            AppendPreflightLine(sb, remoteConfigured ? "OK" : "FAIL", "remote configured", remoteConfigured ? "present in rclone config" : "not found in rclone listremotes");

            if (mounted)
            {
                AppendPreflightLine(sb, "OK", "drive letter", "already mounted by Pixelpipe on " + GetDriveRoot(p));
                string stats = RunRcloneCapture("rc core/stats --rc-addr 127.0.0.1:" + p.RcPort.ToString() + " --rc-no-auth", 3500);
                AppendPreflightLine(sb, String.IsNullOrWhiteSpace(stats) ? "WARN" : "OK", "RC endpoint", String.IsNullOrWhiteSpace(stats) ? "mounted process did not answer stats request" : "mounted process answered stats request");
            }
            else
            {
                bool driveInUse = DriveLetterInUse(drive);
                AppendPreflightLine(sb, driveInUse ? "WARN" : "OK", "drive letter", driveInUse ? drive + " appears to be in use" : drive + " appears available");
                bool portFree = LocalTcpPortAvailable(p.RcPort);
                AppendPreflightLine(sb, portFree ? "OK" : "FAIL", "RC port", portFree ? "127.0.0.1:" + p.RcPort.ToString() + " appears available" : "127.0.0.1:" + p.RcPort.ToString() + " is already in use");
            }

            if (remoteConfigured)
            {
                string about = RunRcloneCapture("about " + QuoteArg(remote) + " --json", 8000);
                if (String.IsNullOrWhiteSpace(about))
                {
                    AppendPreflightLine(sb, "WARN", "storage probe", "rclone about returned no output before timeout");
                }
                else if (about.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || about.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AppendPreflightLine(sb, "WARN", "storage probe", TrimForMenu(OneLine(ScrubSecrets(about)), 220));
                }
                else
                {
                    AppendPreflightLine(sb, "OK", "storage probe", "backend answered rclone about");
                }
            }

            sb.AppendLine();
            if (PreflightHasFailures(sb.ToString())) sb.AppendLine("Result: issues found before mount.");
            else if (PreflightHasWarnings(sb.ToString())) sb.AppendLine("Result: usable, with warnings to review.");
            else sb.AppendLine("Result: ready to mount.");
            return sb.ToString();
        }

        private bool LocalTcpPortAvailable(int port)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch { return false; }
            finally
            {
                try { if (listener != null) listener.Stop(); } catch { }
            }
        }

        private void AppendPreflightLine(StringBuilder sb, string state, string label, string detail)
        {
            sb.AppendLine(FormatPreflightLine(state, label, detail));
        }

        internal static string FormatPreflightLine(string state, string label, string detail)
        {
            string s = String.IsNullOrWhiteSpace(state) ? "INFO" : state.Trim().ToUpperInvariant();
            string l = String.IsNullOrWhiteSpace(label) ? "check" : label.Trim();
            string d = String.IsNullOrWhiteSpace(detail) ? "(no detail)" : detail.Trim();
            return "[" + s + "] " + l + ": " + d;
        }

        internal static bool PreflightHasFailures(string report)
        {
            return !String.IsNullOrEmpty(report) && report.IndexOf("[FAIL]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool PreflightHasWarnings(string report)
        {
            return !String.IsNullOrEmpty(report) && report.IndexOf("[WARN]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string FirstNonEmptyLine(string text)
        {
            if (String.IsNullOrEmpty(text)) return "";
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length > 0) return line;
            }
            return "";
        }

        private static string OneLine(string text)
        {
            return String.IsNullOrEmpty(text) ? "" : text.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
