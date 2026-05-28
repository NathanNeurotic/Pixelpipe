using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        // Drives the scheduled mount/unmount timer. Runs every 30 seconds so we
        // reliably catch each minute boundary without sub-minute precision.
        private System.Windows.Forms.Timer scheduleTimer;

        private void StartScheduleTimer()
        {
            if (scheduleTimer != null) return;
            scheduleTimer = new System.Windows.Forms.Timer();
            scheduleTimer.Interval = 30000;
            scheduleTimer.Tick += delegate { CheckSchedules(); };
            scheduleTimer.Start();
        }

        // For each profile with ScheduleEnabled, check whether the current
        // local-day/time matches a mount or unmount window we haven't fired
        // for today yet. Day-key throttling stops re-firing across the timer's
        // 30-second ticks within the same minute.
        private void CheckSchedules()
        {
            try
            {
                DateTime now = DateTime.Now;
                string today = now.ToString("yyyy-MM-dd");
                string nowHHmm = now.ToString("HH:mm");
                RemoteProfile[] snapshot = SnapshotProfiles();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    RemoteProfile p = snapshot[i];
                    if (!p.ScheduleEnabled) continue;
                    if (!ScheduleAllowsDay(p.ScheduleDays, now.DayOfWeek)) continue;

                    if (!String.IsNullOrEmpty(p.ScheduleMountTime) &&
                        ScheduleTimeMatches(p.ScheduleMountTime, nowHHmm) &&
                        !String.Equals(p.LastScheduleMountKey, today + "@" + p.ScheduleMountTime))
                    {
                        p.LastScheduleMountKey = today + "@" + p.ScheduleMountTime;
                        if (!IsMounted(p))
                        {
                            LogUiInfo("schedule mount", p.Label + " at " + p.ScheduleMountTime);
                            ShowBalloon(p.Label + ": scheduled mount at " + p.ScheduleMountTime);
                            MountProfile(p, p.FullCache);
                        }
                    }

                    if (!String.IsNullOrEmpty(p.ScheduleUnmountTime) &&
                        ScheduleTimeMatches(p.ScheduleUnmountTime, nowHHmm) &&
                        !String.Equals(p.LastScheduleUnmountKey, today + "@" + p.ScheduleUnmountTime))
                    {
                        p.LastScheduleUnmountKey = today + "@" + p.ScheduleUnmountTime;
                        if (IsMounted(p))
                        {
                            LogUiInfo("schedule unmount", p.Label + " at " + p.ScheduleUnmountTime);
                            ShowBalloon(p.Label + ": scheduled unmount at " + p.ScheduleUnmountTime);
                            UnmountProfile(p, true);
                        }
                    }

                    // Bandwidth schedule. Each entry's day-key is "<today>@<time>=<limit>"
                    // so changing the schedule mid-day re-fires correctly and
                    // the same entry won't fire twice within its minute.
                    List<BandwidthScheduleEntry> bwEntries = ParseBandwidthSchedule(p.BandwidthScheduleEntries);
                    for (int b = 0; b < bwEntries.Count; b++)
                    {
                        BandwidthScheduleEntry entry = bwEntries[b];
                        if (!ScheduleTimeMatches(entry.Time, nowHHmm)) continue;
                        string key = today + "@" + entry.Time + "=" + entry.Limit;
                        if (String.Equals(p.LastBandwidthScheduleKey, key)) continue;
                        p.LastBandwidthScheduleKey = key;
                        LogUiInfo("schedule bandwidth", p.Label + " at " + entry.Time + " -> " + DisplayLimit(entry.Limit));
                        SetProfileBandwidth(p, entry.Limit);
                    }
                }
            }
            catch (Exception ex) { LogUiIssue("schedule timer", ex); }
        }

        internal sealed class BandwidthScheduleEntry
        {
            public string Time;
            public string Limit;
        }

        // Pure helper: parses a comma-separated "HH:mm=limit" schedule into
        // a list of normalised entries. Invalid pieces (bad time, bad limit)
        // are silently skipped so a typo in one entry doesn't kill the others.
        // Returns an empty list for null/empty/all-invalid input.
        internal static List<BandwidthScheduleEntry> ParseBandwidthSchedule(string raw)
        {
            List<BandwidthScheduleEntry> result = new List<BandwidthScheduleEntry>();
            if (String.IsNullOrWhiteSpace(raw)) return result;
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (token.Length == 0) continue;
                int eq = token.IndexOf('=');
                if (eq <= 0 || eq == token.Length - 1) continue;
                string timePart = token.Substring(0, eq).Trim();
                string limitPart = token.Substring(eq + 1).Trim();
                string normalisedTime;
                if (!TryNormalizeScheduleTime(timePart, out normalisedTime)) continue;
                if (!IsValidBandwidth(limitPart)) continue;
                BandwidthScheduleEntry entry = new BandwidthScheduleEntry();
                entry.Time = normalisedTime;
                entry.Limit = NormalizeBandwidthLimit(limitPart);
                result.Add(entry);
            }
            return result;
        }

        // True iff dayList (e.g. "Mon,Wed,Fri") contains the abbreviation for
        // the given DayOfWeek. Empty / null dayList is treated as "all days"
        // so older profile files without ScheduleDays still work.
        internal static bool ScheduleAllowsDay(string dayList, DayOfWeek day)
        {
            if (String.IsNullOrWhiteSpace(dayList)) return true;
            string want = DayAbbrev(day);
            string[] parts = dayList.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (String.Equals(parts[i].Trim(), want, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static string DayAbbrev(DayOfWeek d)
        {
            switch (d)
            {
                case DayOfWeek.Monday: return "Mon";
                case DayOfWeek.Tuesday: return "Tue";
                case DayOfWeek.Wednesday: return "Wed";
                case DayOfWeek.Thursday: return "Thu";
                case DayOfWeek.Friday: return "Fri";
                case DayOfWeek.Saturday: return "Sat";
                default: return "Sun";
            }
        }

        // True iff `scheduled` parses as HH:mm and matches `nowHHmm`. Tolerates
        // common variants ("9:00", "09:00", " 09:00 ") and 24-hour input.
        internal static bool ScheduleTimeMatches(string scheduled, string nowHHmm)
        {
            string normalized;
            if (!TryNormalizeScheduleTime(scheduled, out normalized)) return false;
            return String.Equals(normalized, nowHHmm, StringComparison.OrdinalIgnoreCase);
        }

        // Accepts "H:mm", "HH:mm", and pads single-digit hours. Returns false
        // for blank input or anything that can't be a 24-hour time.
        internal static bool TryNormalizeScheduleTime(string input, out string normalized)
        {
            normalized = "";
            if (String.IsNullOrWhiteSpace(input)) return false;
            string s = input.Trim();
            int colon = s.IndexOf(':');
            if (colon <= 0 || colon == s.Length - 1) return false;
            string hh = s.Substring(0, colon).Trim();
            string mm = s.Substring(colon + 1).Trim();
            int h, m;
            if (!Int32.TryParse(hh, out h)) return false;
            if (!Int32.TryParse(mm, out m)) return false;
            if (h < 0 || h > 23) return false;
            if (m < 0 || m > 59) return false;
            normalized = h.ToString("D2") + ":" + m.ToString("D2");
            return true;
        }
    }
}
