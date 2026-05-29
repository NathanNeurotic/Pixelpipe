using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        private const string ProfileExportVersion = "0.9";

        // Tools / diagnostics menu hook: prompts for an output path with
        // SaveFileDialog, writes the profile list as JSON. Encrypted secrets
        // are NOT written — DPAPI blobs decrypt only on the writing Windows
        // user, so they're useless on another machine; we include an
        // explanatory note instead so the user understands they have to
        // re-enter the API key after import.
        private void ExportProfilesToFile()
        {
            try
            {
                RemoteProfile[] snapshot = SnapshotProfiles();
                if (snapshot.Length == 0)
                {
                    MessageBox.Show("There are no profiles to export.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string defaultName = "pixelpipe-profiles-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json";
                string path;
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Title = "Export Pixelpipe profiles";
                    dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                    dlg.FileName = defaultName;
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    path = dlg.FileName;
                }

                string json = BuildProfilesExportJson(snapshot);
                File.WriteAllText(path, json, Encoding.UTF8);

                StringBuilder msg = new StringBuilder();
                msg.AppendLine("Exported " + snapshot.Length + " profile(s) to:");
                msg.AppendLine(path);
                msg.AppendLine();
                msg.AppendLine("API keys and other DPAPI-encrypted secrets are NOT included; you will need to re-enter them on the importing machine.");
                msg.AppendLine("rclone remote configuration is also NOT included; export the rclone config separately if you need to share remotes.");
                MessageBox.Show(msg.ToString(), "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LogUiWarn("export profiles", "wrote " + snapshot.Length + " profile(s) to " + path);
            }
            catch (Exception ex)
            {
                LogUiIssue("export profiles", ex);
                MessageBox.Show("Could not export profiles.\r\n\r\n" + ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Tools / diagnostics menu hook: prompts with OpenFileDialog, parses
        // the file, then shows a checklist letting the user pick which profiles
        // to add. Profiles whose Id already exists are pre-skipped; drive
        // letter and label collisions are resolved automatically.
        private void ImportProfilesFromFile()
        {
            try
            {
                string path;
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Title = "Import Pixelpipe profiles";
                    dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    path = dlg.FileName;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                List<RemoteProfile> incoming;
                string error;
                if (!TryParseProfilesExportJson(json, out incoming, out error))
                {
                    MessageBox.Show("This does not look like a Pixelpipe profiles export:\r\n\r\n" + error, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (incoming.Count == 0)
                {
                    MessageBox.Show("The file did not contain any profiles.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                HashSet<string> existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                RemoteProfile[] existing = SnapshotProfiles();
                for (int i = 0; i < existing.Length; i++) existingIds.Add(existing[i].Id ?? "");

                ImportPlan plan = PlanProfileImport(incoming, existingIds);

                if (plan.NewProfiles.Count == 0 && plan.AlreadyPresent.Count > 0)
                {
                    MessageBox.Show("All " + plan.AlreadyPresent.Count + " profile(s) in the file are already in Pixelpipe.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string[] labels = new string[plan.NewProfiles.Count];
                for (int i = 0; i < plan.NewProfiles.Count; i++)
                {
                    RemoteProfile p = plan.NewProfiles[i];
                    labels[i] = p.Label + " (" + DisplayProvider(p.Provider) + ", " + p.DriveLetter + ")";
                }
                string note = plan.AlreadyPresent.Count == 0
                    ? "Pick a profile to add:"
                    : ("Pick a profile to add (" + plan.AlreadyPresent.Count + " profile(s) already present and skipped):");
                string chosen = ChooseFromList("Import profiles", note, labels);
                if (chosen == null) return;

                int chosenIdx = Array.IndexOf(labels, chosen);
                if (chosenIdx < 0) return;
                RemoteProfile imported = plan.NewProfiles[chosenIdx];

                // Resolve drive letter and label collisions against the live
                // (post-snapshot) state so two quick imports don't collide.
                imported.Label = UniqueLabel(String.IsNullOrWhiteSpace(imported.Label) ? "Imported remote" : imported.Label);
                if (DriveLetterInUse(imported.DriveLetter) || ProfileDriveLetterExists(imported.DriveLetter))
                {
                    imported.DriveLetter = FirstFreePreferredDrive(imported.DriveLetter);
                }

                // Timestamped backup before we modify settings.json so the
                // user can undo an unintended import from Tools / diagnostics
                // → Open settings backups folder.
                BackupSettingsFile("import-" + SafeFileName(imported.Label));
                lock (profilesLock) profiles.Add(imported);
                AssignRuntimeFields();
                SaveProfiles();
                RebuildMenu();
                RebuildMainWindowProfiles();
                ShowBalloon("Imported '" + imported.Label + "'. Remember to configure its rclone remote if it doesn't exist yet.");
            }
            catch (Exception ex)
            {
                LogUiIssue("import profiles", ex);
                MessageBox.Show("Could not import profiles.\r\n\r\n" + ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ProfileDriveLetterExists(string letter)
        {
            string n = NormalizeDriveLetter(letter);
            RemoteProfile[] snapshot = SnapshotProfiles();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (String.Equals(snapshot[i].DriveLetter, n, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal sealed class ImportPlan
        {
            public List<RemoteProfile> NewProfiles = new List<RemoteProfile>();
            public List<RemoteProfile> AlreadyPresent = new List<RemoteProfile>();
        }

        // Pure helper; tests cover the dedup-by-Id behaviour without needing a
        // running TrayContext.
        internal static ImportPlan PlanProfileImport(List<RemoteProfile> incoming, HashSet<string> existingIds)
        {
            ImportPlan plan = new ImportPlan();
            if (incoming == null) return plan;
            for (int i = 0; i < incoming.Count; i++)
            {
                RemoteProfile p = incoming[i];
                if (p == null) continue;
                string id = p.Id ?? "";
                if (id.Length > 0 && existingIds != null && existingIds.Contains(id)) plan.AlreadyPresent.Add(p);
                else plan.NewProfiles.Add(p);
            }
            return plan;
        }

        // Pure helper; tests cover the JSON shape so a future schema change is
        // caught up-front.
        internal static string BuildProfilesExportJson(RemoteProfile[] snapshot)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            Dictionary<string, object> root = new Dictionary<string, object>();

            Dictionary<string, object> meta = new Dictionary<string, object>();
            meta["version"] = ProfileExportVersion;
            meta["exportedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            meta["appVersion"] = Application.ProductVersion ?? "";
            meta["machine"] = Environment.MachineName ?? "";
            root["_pixelpipeExport"] = meta;

            List<object> arr = new List<object>();
            for (int i = 0; i < snapshot.Length; i++)
            {
                RemoteProfile p = snapshot[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["Id"] = p.Id;
                d["Label"] = p.Label;
                d["Provider"] = p.Provider;
                d["Remote"] = NormalizeRemoteName(p.Remote);
                d["DriveLetter"] = NormalizeDriveLetter(p.DriveLetter);
                d["MountMode"] = NormalizeMountMode(p.MountMode);
                d["AutoMount"] = p.AutoMount;
                d["FullCache"] = p.FullCache;
                d["BandwidthLimit"] = p.BandwidthLimit ?? "";
                d["ScheduleEnabled"] = p.ScheduleEnabled;
                d["ScheduleMountTime"] = p.ScheduleMountTime ?? "";
                d["ScheduleUnmountTime"] = p.ScheduleUnmountTime ?? "";
                d["ScheduleDays"] = String.IsNullOrWhiteSpace(p.ScheduleDays) ? "Mon,Tue,Wed,Thu,Fri,Sat,Sun" : p.ScheduleDays;
                d["WatchFolderEnabled"] = p.WatchFolderEnabled;
                d["WatchFolderPath"] = p.WatchFolderPath ?? "";
                d["WatchFolderTargetDir"] = p.WatchFolderTargetDir ?? "";
                d["WatchFolderMode"] = NormalizeWatchMode(p.WatchFolderMode);
                d["WatchFolderQuietMs"] = p.WatchFolderQuietMs > 0 ? p.WatchFolderQuietMs : 5000;
                d["BandwidthScheduleEntries"] = p.BandwidthScheduleEntries ?? "";
                arr.Add(d);
            }
            root["profiles"] = arr.ToArray();

            return js.Serialize(root);
        }

        // Pure helper; returns true on parse success. Tests cover happy path
        // and the obvious failure modes (not-JSON, wrong shape, missing array).
        internal static bool TryParseProfilesExportJson(string json, out List<RemoteProfile> profiles, out string error)
        {
            profiles = new List<RemoteProfile>();
            error = "";
            if (String.IsNullOrWhiteSpace(json)) { error = "empty file"; return false; }
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> root = js.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) { error = "top-level value is not an object"; return false; }
                object profilesObj;
                if (!root.TryGetValue("profiles", out profilesObj))
                {
                    if (!root.TryGetValue("Profiles", out profilesObj))
                    {
                        error = "no \"profiles\" array";
                        return false;
                    }
                }
                object[] arr = profilesObj as object[];
                if (arr == null) { error = "\"profiles\" is not an array"; return false; }
                for (int i = 0; i < arr.Length; i++)
                {
                    Dictionary<string, object> d = arr[i] as Dictionary<string, object>;
                    if (d == null) continue;
                    RemoteProfile p = new RemoteProfile();
                    p.Id = ToStringValue(GetDictValue(d, "Id"), Guid.NewGuid().ToString("N"));
                    p.Label = ToStringValue(GetDictValue(d, "Label"), "Imported remote");
                    p.Provider = NormalizeProvider(ToStringValue(GetDictValue(d, "Provider"), "custom"), ToStringValue(GetDictValue(d, "Remote"), ""));
                    p.Remote = NormalizeRemoteName(ToStringValue(GetDictValue(d, "Remote"), DefaultRemoteName));
                    p.DriveLetter = NormalizeDriveLetter(ToStringValue(GetDictValue(d, "DriveLetter"), DefaultDriveLetter));
                    p.MountMode = NormalizeMountMode(ToStringValue(GetDictValue(d, "MountMode"), "network"));
                    p.AutoMount = ToBool(GetDictValue(d, "AutoMount"));
                    p.FullCache = ToBool(GetDictValue(d, "FullCache"));
                    p.BandwidthLimit = ToStringValue(GetDictValue(d, "BandwidthLimit"), "");
                    p.ScheduleEnabled = ToBool(GetDictValue(d, "ScheduleEnabled"));
                    p.ScheduleMountTime = ToStringValue(GetDictValue(d, "ScheduleMountTime"), "");
                    p.ScheduleUnmountTime = ToStringValue(GetDictValue(d, "ScheduleUnmountTime"), "");
                    p.ScheduleDays = ToStringValue(GetDictValue(d, "ScheduleDays"), "Mon,Tue,Wed,Thu,Fri,Sat,Sun");
                    p.WatchFolderEnabled = ToBool(GetDictValue(d, "WatchFolderEnabled"));
                    p.WatchFolderPath = ToStringValue(GetDictValue(d, "WatchFolderPath"), "");
                    p.WatchFolderTargetDir = ToStringValue(GetDictValue(d, "WatchFolderTargetDir"), "");
                    p.WatchFolderMode = NormalizeWatchMode(ToStringValue(GetDictValue(d, "WatchFolderMode"), "move"));
                    object quietObj = GetDictValue(d, "WatchFolderQuietMs");
                    long quiet = 0;
                    if (quietObj != null)
                    {
                        long parsed;
                        if (Int64.TryParse(Convert.ToString(quietObj, System.Globalization.CultureInfo.InvariantCulture), out parsed)) quiet = parsed;
                    }
                    p.WatchFolderQuietMs = quiet > 0 ? (int)Math.Min(quiet, 600000) : 5000;
                    p.BandwidthScheduleEntries = ToStringValue(GetDictValue(d, "BandwidthScheduleEntries"), "");
                    profiles.Add(p);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

    }
}
