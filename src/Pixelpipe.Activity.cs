using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Pixelpipe
{
    // One parsed event from pixelpipe-ui.log. Times come straight off the
    // log line and are local-time (the log writes them as such). Category is
    // a coarse bucket derived from the line content — useful for filtering
    // the noise out of the live "what just happened?" pane in the main
    // window's Activity tab.
    internal sealed class ActivityEvent
    {
        public DateTime Time;
        public string Category;
        public string Message;
        public string RawLine;
    }

    internal sealed partial class TrayContext
    {
        // PERF-4 (v0.13.1): static compiled regex for the log-line parser
        // so a several-hundred-line Activity refresh doesn't recompile it.
        private static readonly Regex ActivityLineRegex = new Regex(
            @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s+\[(?<level>[^\]]+)\]\s*(\[(?<area>[^\]]+)\])?\s*(?<msg>.*)$",
            RegexOptions.Compiled);

        // Pure helper, tests cover it. Walks the log content line-by-line
        // and returns the most-recent `maxEvents` events first. Lines that
        // don't parse are skipped silently — the log occasionally has
        // multi-line tracebacks that we just ignore here.
        internal static List<ActivityEvent> ParseActivityLog(string logContent, int maxEvents)
        {
            List<ActivityEvent> all = new List<ActivityEvent>();
            if (String.IsNullOrEmpty(logContent)) return all;
            string[] lines = logContent.Replace("\r", "").Split('\n');
            // Format: "YYYY-MM-DD HH:MM:SS [level] [area] message"
            Regex re = ActivityLineRegex;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                Match m = re.Match(line);
                if (!m.Success) continue;
                DateTime t;
                if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t)) continue;
                string area = m.Groups["area"].Success ? m.Groups["area"].Value : "";
                string msg = m.Groups["msg"].Value ?? "";
                string level = m.Groups["level"].Value ?? "";
                ActivityEvent ev = new ActivityEvent();
                ev.Time = t;
                ev.RawLine = line;
                ev.Category = ClassifyActivity(level, area, msg);
                ev.Message = (area.Length > 0 ? area + ": " : "") + msg;
                all.Add(ev);
            }
            // Most recent first; cap to maxEvents.
            all.Reverse();
            if (maxEvents > 0 && all.Count > maxEvents) all.RemoveRange(maxEvents, all.Count - maxEvents);
            return all;
        }

        // Pure helper, tests cover it. Pattern-matches the most common
        // log shapes Pixelpipe writes today so the Activity tab can
        // colour-code / filter by category without re-parsing every time
        // the user changes the filter dropdown.
        internal static string ClassifyActivity(string level, string area, string message)
        {
            string lvl = (level ?? "").ToLowerInvariant();
            string ar = (area ?? "").ToLowerInvariant();
            string ms = (message ?? "").ToLowerInvariant();
            if (lvl == "error") return "Error";
            // Order matters: "schedule mount" must classify as Schedule, not
            // Mount. Same for "schedule unmount" / "schedule bandwidth".
            if (ar.IndexOf("schedule") >= 0) return "Schedule";
            if (ar.IndexOf("mount") >= 0 && ar.IndexOf("unmount") < 0) return "Mount";
            if (ar.IndexOf("unmount") >= 0) return "Unmount";
            if (ar.IndexOf("watch") >= 0) return "Watch";
            if (ar.IndexOf("orphan") >= 0) return "Orphan";
            if (ar.IndexOf("settings backup") >= 0 || ar.IndexOf("backup") >= 0) return "Backup";
            if (ar.IndexOf("update") >= 0) return "Update";
            if (ar.IndexOf("rclone job") >= 0) return "Startup";
            if (ms.IndexOf("transfer finished") >= 0 || ar.IndexOf("transfer") >= 0) return "Transfer";
            if (lvl == "warn") return "Warning";
            return "Other";
        }

        // Wraps log read + parse. Called by the Activity tab refresh path.
        internal List<ActivityEvent> ReadActivityEvents(int maxEvents)
        {
            try
            {
                if (!File.Exists(uiLogFile)) return new List<ActivityEvent>();
                string content;
                using (FileStream fs = new FileStream(uiLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8)) content = reader.ReadToEnd();
                return ParseActivityLog(content, maxEvents);
            }
            catch (Exception ex) { LogUiIssue("read activity log", ex); return new List<ActivityEvent>(); }
        }

        // Formats events for the read-only TextBox. Returns a single string
        // with one event per line, padded so the columns align visually.
        internal static string FormatActivityEvents(List<ActivityEvent> events, string categoryFilter)
        {
            if (events == null || events.Count == 0) return "(no activity yet — try mounting a profile or letting the schedule fire)";
            StringBuilder sb = new StringBuilder();
            bool allCats = String.IsNullOrEmpty(categoryFilter) || String.Equals(categoryFilter, "All", StringComparison.OrdinalIgnoreCase);
            int kept = 0;
            for (int i = 0; i < events.Count; i++)
            {
                ActivityEvent ev = events[i];
                if (!allCats && !String.Equals(ev.Category, categoryFilter, StringComparison.OrdinalIgnoreCase)) continue;
                sb.Append(ev.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.Append("  ");
                sb.Append(ev.Category.PadRight(9));
                sb.Append("  ");
                sb.AppendLine(ev.Message);
                kept++;
            }
            if (kept == 0) return "(no events match filter '" + categoryFilter + "')";
            return sb.ToString();
        }
    }
}
