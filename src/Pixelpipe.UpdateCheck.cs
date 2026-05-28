using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        // GitHub release tag detected on the last successful check that's newer
        // than the running build. Empty when up-to-date or never checked.
        // Cleared when the user opens the linked release page so the menu item
        // doesn't linger after they've seen it.
        private string availableUpdateVersion;
        private int updateCheckFlag; // Interlocked: 0 = idle, 1 = check in flight
        private const string UpdateCheckUrl = "https://api.github.com/repos/NathanNeurotic/Pixelpipe/releases/latest";
        private const string ReleasesPageUrl = "https://github.com/NathanNeurotic/Pixelpipe/releases/latest";

        // Called from OnMenuOpening so we get a passive check whenever the user
        // interacts with the tray. No-op if disabled, if a check is already in
        // flight, or if we already checked in the last 24 hours.
        private void CheckForUpdatesIfDue()
        {
            if (!String.Equals(LoadSetting("UpdateCheckEnabled", "1"), "1", StringComparison.OrdinalIgnoreCase)) return;
            DateTime last;
            string lastStr = LoadSetting("LastUpdateCheckUtc", "");
            if (!DateTime.TryParse(lastStr, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out last)) last = DateTime.MinValue;
            if ((DateTime.UtcNow - last).TotalHours < 24) return;
            if (Interlocked.CompareExchange(ref updateCheckFlag, 1, 0) != 0) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                string latestTag = "";
                try { latestTag = FetchLatestReleaseTag(); }
                catch (Exception ex) { LogUiIssue("update check", ex); }

                BeginUi(delegate
                {
                    try
                    {
                        SaveSetting("LastUpdateCheckUtc", DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
                        string localVersion = Application.ProductVersion;
                        if (IsNewer(latestTag, localVersion))
                        {
                            string previous = availableUpdateVersion ?? "";
                            availableUpdateVersion = latestTag;
                            SaveSetting("AvailableUpdateVersion", latestTag);
                            // Balloon only on transitions so we don't nag the user on every refresh.
                            if (!String.Equals(previous, latestTag, StringComparison.OrdinalIgnoreCase))
                            {
                                ShowBalloon("Pixelpipe " + latestTag + " is available. Open the tray menu to download.");
                            }
                            RebuildMenu();
                        }
                        else
                        {
                            if (!String.IsNullOrEmpty(availableUpdateVersion))
                            {
                                availableUpdateVersion = "";
                                SaveSetting("AvailableUpdateVersion", "");
                                RebuildMenu();
                            }
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref updateCheckFlag, 0);
                    }
                });
            });
        }

        private string FetchLatestReleaseTag()
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(UpdateCheckUrl);
            req.Method = "GET";
            req.Timeout = 10000;
            req.ReadWriteTimeout = 10000;
            req.UserAgent = "Pixelpipe/" + Application.ProductVersion + " (update-check)";
            req.Accept = "application/vnd.github+json";
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream stream = resp.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> root = js.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return "";
                return ToStringValue(GetDictValue(root, "tag_name"), "");
            }
        }

        // True only when remoteTag parses as a higher semver than localVersion.
        // Tolerates a leading "v" / "V" on the tag and gracefully returns false
        // for unparseable or empty inputs.
        internal static bool IsNewer(string remoteTag, string localVersion)
        {
            if (String.IsNullOrEmpty(remoteTag) || String.IsNullOrEmpty(localVersion)) return false;
            string r = remoteTag.Trim();
            if (r.Length > 0 && (r[0] == 'v' || r[0] == 'V')) r = r.Substring(1);
            Version remote;
            Version local;
            if (!Version.TryParse(r, out remote)) return false;
            if (!Version.TryParse(localVersion, out local)) return false;
            // Pad missing components so 0.7.0 vs 0.7.0.0 compare equal (csc emits a 4-part
            // AssemblyVersion; the GitHub tag uses 3-part semver).
            int rMajor = Math.Max(0, remote.Major), rMinor = Math.Max(0, remote.Minor), rBuild = Math.Max(0, remote.Build), rRev = Math.Max(0, remote.Revision);
            int lMajor = Math.Max(0, local.Major), lMinor = Math.Max(0, local.Minor), lBuild = Math.Max(0, local.Build), lRev = Math.Max(0, local.Revision);
            if (rMajor != lMajor) return rMajor > lMajor;
            if (rMinor != lMinor) return rMinor > lMinor;
            if (rBuild != lBuild) return rBuild > lBuild;
            return rRev > lRev;
        }

        // Opens the releases page and clears the "available update" indicator.
        private void OpenAvailableUpdate()
        {
            try { Process.Start(ReleasesPageUrl); }
            catch (Exception ex) { LogUiIssue("open releases page", ex); }
            availableUpdateVersion = "";
            SaveSetting("AvailableUpdateVersion", "");
            RebuildMenu();
        }
    }
}
