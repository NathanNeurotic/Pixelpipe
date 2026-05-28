using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        // One entry per file the watcher has seen. ReadyAtUtc starts in the
        // future (now + QuietMs) and is bumped forward whenever the file is
        // touched again, so the uploader doesn't grab a file that's still
        // actively being written. Attempt counts retries; after MaxAttempts
        // we give up and log.
        private sealed class WatchEntry
        {
            public string ProfileId;
            public string LocalPath;
            public DateTime ReadyAtUtc;
            public int Attempts;
            public long LastSeenSize;
        }

        private readonly object watchLock = new object();
        // ProfileId → file path → entry. Stable within a profile because
        // FileSystemWatcher only emits one path per event.
        private readonly Dictionary<string, Dictionary<string, WatchEntry>> watchQueues = new Dictionary<string, Dictionary<string, WatchEntry>>(StringComparer.OrdinalIgnoreCase);
        // ProfileId → currently-uploading file paths, so we don't re-queue a
        // path the uploader has already picked up.
        private readonly Dictionary<string, HashSet<string>> watchInflight = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        // ProfileId → FileSystemWatcher; we dispose and rebuild when the
        // profile's WatchFolderPath or WatchFolderEnabled changes.
        private readonly Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);

        private System.Windows.Forms.Timer watchTimer;
        // Max parallel uploads per profile. Two keeps small files moving while
        // a single large upload is in flight without saturating a typical
        // home upstream.
        private const int WatchMaxConcurrentPerProfile = 2;
        // Retry backoff sequence in seconds; entries beyond the last value
        // re-use the last value. After WatchMaxAttempts we drop the entry.
        private static readonly int[] WatchRetryBackoffSec = new int[] { 30, 120, 600 };
        private const int WatchMaxAttempts = 3;

        // Called from the TrayContext constructor. Sets up the global 3-second
        // timer and a watcher per enabled profile.
        private void StartWatchFolders()
        {
            try
            {
                ReconcileAllWatchers();
                watchTimer = new System.Windows.Forms.Timer();
                watchTimer.Interval = 3000;
                watchTimer.Tick += delegate { TryProcessWatchQueues(); };
                watchTimer.Start();
            }
            catch (Exception ex) { LogUiIssue("watch folder start", ex); }
        }

        // Rebuilds the FileSystemWatcher set from the current profile list.
        // Called whenever a profile is added, removed, or edited.
        private void ReconcileAllWatchers()
        {
            try
            {
                RemoteProfile[] snapshot = SnapshotProfiles();
                lock (watchLock)
                {
                    HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < snapshot.Length; i++)
                    {
                        RemoteProfile p = snapshot[i];
                        if (p == null || !p.WatchFolderEnabled) continue;
                        if (String.IsNullOrWhiteSpace(p.WatchFolderPath)) continue;
                        if (!Directory.Exists(p.WatchFolderPath))
                        {
                            // Don't watch a non-existent path; surface it on the
                            // profile so the UI tells the user.
                            p.WatchLastResult = "watch path does not exist: " + p.WatchFolderPath;
                            p.WatchLastResultUtc = DateTime.UtcNow;
                            continue;
                        }
                        wanted.Add(p.Id);
                        FileSystemWatcher existing;
                        if (watchers.TryGetValue(p.Id, out existing))
                        {
                            if (String.Equals(existing.Path, p.WatchFolderPath, StringComparison.OrdinalIgnoreCase))
                            {
                                continue; // already watching the right path
                            }
                            existing.EnableRaisingEvents = false;
                            existing.Dispose();
                            watchers.Remove(p.Id);
                        }
                        FileSystemWatcher fsw = BuildWatcher(p);
                        if (fsw != null) watchers[p.Id] = fsw;
                    }
                    // Tear down watchers for profiles no longer enabled.
                    List<string> toRemove = new List<string>();
                    foreach (KeyValuePair<string, FileSystemWatcher> kv in watchers)
                    {
                        if (!wanted.Contains(kv.Key)) toRemove.Add(kv.Key);
                    }
                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        FileSystemWatcher w = watchers[toRemove[i]];
                        try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
                        watchers.Remove(toRemove[i]);
                        watchQueues.Remove(toRemove[i]);
                        watchInflight.Remove(toRemove[i]);
                    }
                }
            }
            catch (Exception ex) { LogUiIssue("watch folder reconcile", ex); }
        }

        private FileSystemWatcher BuildWatcher(RemoteProfile p)
        {
            try
            {
                FileSystemWatcher fsw = new FileSystemWatcher(p.WatchFolderPath);
                fsw.IncludeSubdirectories = false;
                fsw.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                fsw.Created += delegate (object s, FileSystemEventArgs e) { OnWatchFileTouched(p, e.FullPath); };
                fsw.Changed += delegate (object s, FileSystemEventArgs e) { OnWatchFileTouched(p, e.FullPath); };
                fsw.Renamed += delegate (object s, RenamedEventArgs e) { OnWatchFileTouched(p, e.FullPath); };
                fsw.EnableRaisingEvents = true;
                LogUiWarn("watch folder", "watching " + p.WatchFolderPath + " for profile " + p.Label);
                return fsw;
            }
            catch (Exception ex) { LogUiIssue("watch folder build " + p.Label, ex); return null; }
        }

        private void OnWatchFileTouched(RemoteProfile p, string fullPath)
        {
            if (fullPath == null) return;
            try
            {
                if (!File.Exists(fullPath)) return;
                FileInfo info = new FileInfo(fullPath);
                if (info.Length == 0) return; // skip zero-byte placeholders
                lock (watchLock)
                {
                    Dictionary<string, WatchEntry> queue;
                    if (!watchQueues.TryGetValue(p.Id, out queue))
                    {
                        queue = new Dictionary<string, WatchEntry>(StringComparer.OrdinalIgnoreCase);
                        watchQueues[p.Id] = queue;
                    }
                    HashSet<string> inflight;
                    if (!watchInflight.TryGetValue(p.Id, out inflight))
                    {
                        inflight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        watchInflight[p.Id] = inflight;
                    }
                    if (inflight.Contains(fullPath)) return;
                    WatchEntry entry;
                    int quiet = p.WatchFolderQuietMs > 0 ? p.WatchFolderQuietMs : 5000;
                    DateTime ready = DateTime.UtcNow.AddMilliseconds(quiet);
                    if (queue.TryGetValue(fullPath, out entry))
                    {
                        entry.ReadyAtUtc = ready;
                        entry.LastSeenSize = info.Length;
                    }
                    else
                    {
                        entry = new WatchEntry();
                        entry.ProfileId = p.Id;
                        entry.LocalPath = fullPath;
                        entry.ReadyAtUtc = ready;
                        entry.Attempts = 0;
                        entry.LastSeenSize = info.Length;
                        queue[fullPath] = entry;
                    }
                    p.WatchQueueCount = queue.Count;
                }
            }
            catch (Exception ex) { LogUiIssue("watch folder touch " + p.Label, ex); }
        }

        // Walks every profile's queue, picks entries ready to upload, and
        // launches up to WatchMaxConcurrentPerProfile parallel rclone moves.
        // Called from the 3s timer on the UI thread; the actual rclone process
        // runs on a worker thread.
        private void TryProcessWatchQueues()
        {
            try
            {
                RemoteProfile[] snapshot = SnapshotProfiles();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    RemoteProfile p = snapshot[i];
                    if (p == null || !p.WatchFolderEnabled) continue;
                    List<WatchEntry> ready = new List<WatchEntry>();
                    lock (watchLock)
                    {
                        Dictionary<string, WatchEntry> queue;
                        if (!watchQueues.TryGetValue(p.Id, out queue) || queue.Count == 0) continue;
                        HashSet<string> inflight;
                        if (!watchInflight.TryGetValue(p.Id, out inflight))
                        {
                            inflight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            watchInflight[p.Id] = inflight;
                        }
                        int budget = WatchMaxConcurrentPerProfile - inflight.Count;
                        if (budget <= 0) continue;
                        DateTime now = DateTime.UtcNow;
                        foreach (KeyValuePair<string, WatchEntry> kv in queue)
                        {
                            if (ready.Count >= budget) break;
                            if (inflight.Contains(kv.Key)) continue;
                            if (kv.Value.ReadyAtUtc > now) continue;
                            ready.Add(kv.Value);
                        }
                        for (int k = 0; k < ready.Count; k++)
                        {
                            inflight.Add(ready[k].LocalPath);
                            queue.Remove(ready[k].LocalPath);
                        }
                        p.WatchQueueCount = queue.Count;
                        p.WatchUploadingCount = inflight.Count;
                    }
                    for (int k = 0; k < ready.Count; k++)
                    {
                        WatchEntry entry = ready[k];
                        ThreadPool.QueueUserWorkItem(delegate (object state) { UploadWatchEntry(p, (WatchEntry)state); }, entry);
                    }
                }
            }
            catch (Exception ex) { LogUiIssue("watch folder timer", ex); }
        }

        private void UploadWatchEntry(RemoteProfile p, WatchEntry entry)
        {
            string args = BuildWatchUploadArgs(p, entry.LocalPath);
            string output = RunRcloneCapture(args, 1800000); // 30 min ceiling per file
            bool success = !LooksLikeRcloneError(output);
            if (success)
            {
                BeginUi(delegate
                {
                    p.WatchUploadedTotal++;
                    p.WatchLastResult = "uploaded " + Path.GetFileName(entry.LocalPath);
                    p.WatchLastResultUtc = DateTime.UtcNow;
                    ShowBalloon(p.Label + ": watch-folder uploaded " + Path.GetFileName(entry.LocalPath));
                });
                LogUiWarn("watch upload", p.Label + ": uploaded " + entry.LocalPath);
            }
            else
            {
                BeginUi(delegate
                {
                    p.WatchFailedTotal++;
                    p.WatchLastResult = "failed: " + FirstNonEmptyLine(ScrubSecrets(output ?? ""));
                    p.WatchLastResultUtc = DateTime.UtcNow;
                });
                LogUiWarn("watch upload", p.Label + ": failed to upload " + entry.LocalPath + " -- " + ScrubSecrets(output ?? ""));
                entry.Attempts++;
                if (entry.Attempts < WatchMaxAttempts)
                {
                    entry.ReadyAtUtc = ComputeWatchNextRetryUtc(entry.Attempts, DateTime.UtcNow);
                    lock (watchLock)
                    {
                        Dictionary<string, WatchEntry> queue;
                        if (!watchQueues.TryGetValue(p.Id, out queue))
                        {
                            queue = new Dictionary<string, WatchEntry>(StringComparer.OrdinalIgnoreCase);
                            watchQueues[p.Id] = queue;
                        }
                        queue[entry.LocalPath] = entry;
                    }
                }
            }
            lock (watchLock)
            {
                HashSet<string> inflight;
                if (watchInflight.TryGetValue(p.Id, out inflight)) inflight.Remove(entry.LocalPath);
                Dictionary<string, WatchEntry> queue;
                if (watchQueues.TryGetValue(p.Id, out queue)) p.WatchQueueCount = queue.Count;
                p.WatchUploadingCount = inflight == null ? 0 : inflight.Count;
            }
        }

        // Pure helper: builds the rclone moveto/copyto command for a single
        // watched file. The remote-side path is the target dir + the file
        // basename. Tests cover the argument quoting.
        internal static string BuildWatchUploadArgs(RemoteProfile p, string localPath)
        {
            string mode = NormalizeWatchMode(p == null ? "" : p.WatchFolderMode);
            string verb = mode == "copy" ? "copyto" : "moveto";
            string fileName = Path.GetFileName(localPath ?? "");
            string targetDir = (p == null ? "" : (p.WatchFolderTargetDir ?? "")).Trim();
            string remoteName = NormalizeRemoteName(p == null ? DefaultRemoteName : p.Remote);
            string remoteTarget;
            if (targetDir.Length == 0) remoteTarget = remoteName + fileName;
            else
            {
                string normalisedDir = targetDir.Replace('\\', '/').Trim('/');
                remoteTarget = remoteName + (normalisedDir.Length == 0 ? "" : normalisedDir + "/") + fileName;
            }
            return verb + " " + QuoteArg(localPath ?? "") + " " + QuoteArg(remoteTarget);
        }

        // Pure helper: maps "move" / "copy" / anything else to a single
        // canonical value. Defaults to "move".
        internal static string NormalizeWatchMode(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            return m == "copy" ? "copy" : "move";
        }

        // Pure helper: maps an attempt count to the next retry time. Attempt 1
        // uses the first backoff value, attempt 2 the second, and so on, with
        // anything past the array cap clamping to the final value.
        internal static DateTime ComputeWatchNextRetryUtc(int attempt, DateTime baseUtc)
        {
            if (attempt < 1) attempt = 1;
            int idx = attempt - 1;
            if (idx >= WatchRetryBackoffSec.Length) idx = WatchRetryBackoffSec.Length - 1;
            return baseUtc.AddSeconds(WatchRetryBackoffSec[idx]);
        }

        private bool LooksLikeRcloneError(string output)
        {
            if (String.IsNullOrEmpty(output)) return false;
            if (output.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (output.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (output.IndexOf("couldn't", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
