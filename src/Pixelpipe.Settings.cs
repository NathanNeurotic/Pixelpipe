using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        private List<RemoteProfile> LoadProfiles()
        {
            List<RemoteProfile> result = new List<RemoteProfile>();
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                object profilesObj;
                if (root.TryGetValue("Profiles", out profilesObj))
                {
                    object[] arr = profilesObj as object[];
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            Dictionary<string, object> d = arr[i] as Dictionary<string, object>;
                            if (d == null) continue;
                            RemoteProfile p = new RemoteProfile();
                            p.Id = ToStringValue(GetDictValue(d, "Id"), Guid.NewGuid().ToString("N"));
                            p.Label = ToStringValue(GetDictValue(d, "Label"), "Remote");
                            p.Provider = ToStringValue(GetDictValue(d, "Provider"), "custom");
                            p.Remote = ToStringValue(GetDictValue(d, "Remote"), DefaultRemoteName);
                            p.DriveLetter = ToStringValue(GetDictValue(d, "DriveLetter"), DefaultDriveLetter);
                            p.MountMode = ToStringValue(GetDictValue(d, "MountMode"), "network");
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
                            long quiet = ToLong(GetDictValue(d, "WatchFolderQuietMs"));
                            p.WatchFolderQuietMs = quiet > 0 ? (int)Math.Min(quiet, 600000) : 5000;
                            p.BandwidthScheduleEntries = ToStringValue(GetDictValue(d, "BandwidthScheduleEntries"), "");
                            result.Add(p);
                        }
                    }
                }
            }
            catch (Exception ex) { LogUiIssue("load profiles", ex); }

            if (result.Count == 0)
            {
                RemoteProfile p = new RemoteProfile();
                p.Label = "Pixeldrain";
                p.Provider = "pixeldrain";
                p.Remote = NormalizeRemoteName(LoadSetting("RemoteName", DefaultRemoteName));
                p.DriveLetter = NormalizeDriveLetter(LoadSetting("DriveLetter", DefaultDriveLetter));
                p.MountMode = NormalizeMountMode(LoadSetting("MountMode", "network"));
                p.AutoMount = false;
                result.Add(p);
            }
            return result;
        }

        private void SaveProfiles()
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                List<object> list = new List<object>();
                RemoteProfile[] snapshot = SnapshotProfiles();
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
                    list.Add(d);
                }
                root["Profiles"] = list.ToArray();
                root["BandwidthLimit"] = selectedBandwidth;
                WriteSettingsRoot(root);
                // Profile mutations might have added/removed/changed a watch
                // folder; reconcile so the FileSystemWatcher set matches the
                // new state before the timer's next tick.
                ReconcileAllWatchers();
            }
            catch (Exception ex) { LogUiIssue("save profiles", ex); }
        }

        private Dictionary<string, object> ReadSettingsRoot()
        {
            Dictionary<string, object> root;
            if (TryReadSettingsRoot(settingsFile, out root)) return root;

            string backupFile = settingsFile + ".bak";
            if (TryReadSettingsRoot(backupFile, out root))
            {
                LogUiWarn("read settings", "loaded backup settings file after primary file could not be read");
                return root;
            }

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        private bool TryReadSettingsRoot(string path, out Dictionary<string, object> root)
        {
            root = null;
            try
            {
                if (!File.Exists(path)) return false;
                string json = File.ReadAllText(path, Encoding.UTF8);
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> parsed = js.DeserializeObject(json) as Dictionary<string, object>;
                if (parsed == null) return false;
                root = new Dictionary<string, object>(parsed, StringComparer.OrdinalIgnoreCase);
                return true;
            }
            catch (Exception ex) { LogUiIssue("read settings " + Path.GetFileName(path), ex); }
            return false;
        }

        private void WriteSettingsRoot(Dictionary<string, object> root)
        {
            try
            {
                Directory.CreateDirectory(settingsDir);
                JavaScriptSerializer js = new JavaScriptSerializer();
                WriteAllTextAtomic(settingsFile, js.Serialize(root), Encoding.UTF8);
            }
            catch (Exception ex) { LogUiIssue("write settings", ex); }
        }

        // Pixelpipe already keeps the most recent settings.json as .bak via
        // WriteAllTextAtomic (it's overwritten on every save). Before any
        // *user-initiated destructive* operation (profile removal, import
        // that replaces existing profiles, bulk drive-letter changes), we
        // also write a timestamped backup under `backups/` so the user can
        // recover a known-good state hours or days later. The directory is
        // pruned to the last `BackupRetentionCount` files so it doesn't
        // grow unbounded.
        private const int BackupRetentionCount = 20;

        internal string BackupsDir { get { return Path.Combine(settingsDir, "backups"); } }

        private string BackupSettingsFile(string reason)
        {
            try
            {
                if (!File.Exists(settingsFile)) return null;
                Directory.CreateDirectory(BackupsDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string safeReason = SafeFileName(String.IsNullOrEmpty(reason) ? "manual" : reason);
                string target = Path.Combine(BackupsDir, "settings-" + stamp + "-" + safeReason + ".json");
                File.Copy(settingsFile, target, false);
                PruneOldBackups();
                LogUiWarn("settings backup", "wrote " + target);
                return target;
            }
            catch (Exception ex) { LogUiIssue("settings backup " + reason, ex); return null; }
        }

        private void PruneOldBackups()
        {
            try
            {
                if (!Directory.Exists(BackupsDir)) return;
                string[] files = Directory.GetFiles(BackupsDir, "settings-*.json");
                if (files.Length <= BackupRetentionCount) return;
                Array.Sort(files, delegate(string a, string b) { return File.GetCreationTimeUtc(b).CompareTo(File.GetCreationTimeUtc(a)); });
                for (int i = BackupRetentionCount; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
            }
            catch (Exception ex) { LogUiIssue("prune backups", ex); }
        }

        // Tools / diagnostics menu hook.
        private void OpenSettingsBackupsFolder()
        {
            try
            {
                Directory.CreateDirectory(BackupsDir);
                System.Diagnostics.Process.Start(BackupsDir);
            }
            catch (Exception ex) { System.Windows.Forms.MessageBox.Show(ex.Message, "Pixelpipe", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error); }
        }

        internal static void WriteAllTextAtomic(string path, string text, Encoding encoding)
        {
            string temp = path + ".tmp";
            string backup = path + ".bak";
            File.WriteAllText(temp, text ?? "", encoding);
            try
            {
                if (File.Exists(path))
                {
                    try { File.Copy(path, backup, true); } catch { }
                    try
                    {
                        File.Replace(temp, path, backup, true);
                        return;
                    }
                    catch { }
                    File.Delete(path);
                }
                File.Move(temp, path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private string LoadSettingRaw(string name)
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                object v;
                if (root.TryGetValue(name, out v) && v != null) return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\" + AppName, false))
                {
                    object v = key == null ? null : key.GetValue(name);
                    if (v != null) return v.ToString();
                }
            }
            catch { }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\" + LegacyAppName, false))
                {
                    object v = key == null ? null : key.GetValue(name);
                    if (v != null) return v.ToString();
                }
            }
            catch { }
            return "";
        }

        private string LoadSetting(string name, string defaultValue)
        {
            string value = LoadSettingRaw(name);
            return String.IsNullOrEmpty(value) ? defaultValue : value;
        }

        private void SaveSetting(string name, string value)
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                root[name] = value ?? "";
                WriteSettingsRoot(root);
            }
            catch (Exception ex) { LogUiIssue("save setting " + name, ex); }
        }

        private void DeleteSetting(string name)
        {
            try
            {
                Dictionary<string, object> root = ReadSettingsRoot();
                if (root.ContainsKey(name)) root.Remove(name);
                WriteSettingsRoot(root);
            }
            catch (Exception ex) { LogUiIssue("delete setting (json) " + name, ex); }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\" + AppName)) { key.DeleteValue(name, false); }
            }
            catch (Exception ex) { LogUiIssue("delete setting (registry) " + name, ex); }
        }
    }
}
