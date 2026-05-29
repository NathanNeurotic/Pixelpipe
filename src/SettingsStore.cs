using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Pixelpipe
{
    // ARCH-1 step 2 (v0.15.2, audit): second collaborator extracted from the
    // TrayContext partial. Owns the settings.json read / write / cache /
    // atomic-write / timestamped-backup machinery.
    //
    // Profile-specific shape logic (the LoadProfiles / SaveProfiles methods
    // that know which keys live in each profile dict) stays in TrayContext;
    // SettingsStore is the layer below that — give it the root dict, it
    // hands you back the root dict, it handles disk.
    //
    // The locking + cache pattern from PERF-3 lives here unchanged: parsed
    // root dict cached after first read, lock-protected so worker threads
    // can write concurrently without tearing the dict. Atomic-write goes
    // through WriteAllTextAtomic so durability is unchanged.
    internal sealed class SettingsStore
    {
        private readonly string _settingsFile;
        private readonly string _settingsDir;
        private readonly string _backupsDir;
        private readonly Action<string, Exception> _logIssue;
        private readonly Action<string, string> _logWarn;
        private readonly object _cacheLock = new object();
        private Dictionary<string, object> _cache;

        public const int BackupRetentionCount = 20;

        public SettingsStore(string settingsFile, Action<string, Exception> logIssue, Action<string, string> logWarn)
        {
            _settingsFile = settingsFile;
            _settingsDir = Path.GetDirectoryName(settingsFile) ?? "";
            _backupsDir = Path.Combine(_settingsDir, "backups");
            _logIssue = logIssue;
            _logWarn = logWarn;
        }

        public string SettingsFile { get { return _settingsFile; } }
        public string BackupsDir { get { return _backupsDir; } }

        public Dictionary<string, object> ReadRoot()
        {
            lock (_cacheLock)
            {
                if (_cache != null) return _cache;
                Dictionary<string, object> root;
                if (TryReadFile(_settingsFile, out root))
                {
                    _cache = root;
                    return _cache;
                }
                string backupFile = _settingsFile + ".bak";
                if (TryReadFile(backupFile, out root))
                {
                    if (_logWarn != null) _logWarn("read settings", "loaded backup settings file after primary file could not be read");
                    _cache = root;
                    return _cache;
                }
                _cache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                return _cache;
            }
        }

        public void WriteRoot(Dictionary<string, object> root)
        {
            try
            {
                Directory.CreateDirectory(_settingsDir);
                string json;
                lock (_cacheLock)
                {
                    _cache = root;
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    json = js.Serialize(root);
                }
                TrayContext.WriteAllTextAtomic(_settingsFile, json, Encoding.UTF8);
            }
            catch (Exception ex) { if (_logIssue != null) _logIssue("write settings", ex); }
        }

        // Pass-through for the "is anything in settings.json" probe used by
        // the welcome balloon path. Avoids parsing the file when callers only
        // care about presence.
        public bool Exists() { return File.Exists(_settingsFile); }

        // Timestamped backup before destructive operations. Returns the
        // target path on success, null on failure (logged). Prunes the
        // backups directory to BackupRetentionCount entries.
        public string Backup(string reason)
        {
            try
            {
                if (!File.Exists(_settingsFile)) return null;
                Directory.CreateDirectory(_backupsDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string safeReason = TrayContext.SafeFileName(String.IsNullOrEmpty(reason) ? "manual" : reason);
                string target = Path.Combine(_backupsDir, "settings-" + stamp + "-" + safeReason + ".json");
                File.Copy(_settingsFile, target, false);
                Prune();
                if (_logWarn != null) _logWarn("settings backup", "wrote " + target);
                return target;
            }
            catch (Exception ex) { if (_logIssue != null) _logIssue("settings backup " + reason, ex); return null; }
        }

        private void Prune()
        {
            try
            {
                if (!Directory.Exists(_backupsDir)) return;
                string[] files = Directory.GetFiles(_backupsDir, "settings-*.json");
                if (files.Length <= BackupRetentionCount) return;
                Array.Sort(files, delegate (string a, string b) { return File.GetCreationTimeUtc(b).CompareTo(File.GetCreationTimeUtc(a)); });
                for (int i = BackupRetentionCount; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
            }
            catch (Exception ex) { if (_logIssue != null) _logIssue("prune backups", ex); }
        }

        private bool TryReadFile(string path, out Dictionary<string, object> root)
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
            catch (Exception ex) { if (_logIssue != null) _logIssue("read settings " + Path.GetFileName(path), ex); }
            return false;
        }
    }
}
