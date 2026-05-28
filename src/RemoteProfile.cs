using System;
using System.Diagnostics;
using System.Web.Script.Serialization;

namespace Pixelpipe
{
    internal sealed class RemoteProfile
    {
        public string Id;
        public string Label;
        public string Provider;
        public string Remote;
        public string DriveLetter;
        public string MountMode;
        public bool AutoMount;
        public bool FullCache;
        // Per-profile bandwidth override. Empty / null = inherit the global
        // BandwidthLimit setting. Same validation as the global limit.
        public string BandwidthLimit;
        // Scheduled mount/unmount. ScheduleEnabled gates the whole thing.
        // ScheduleMountTime / ScheduleUnmountTime are "HH:mm" local time; either
        // can be empty to skip that side of the schedule. ScheduleDays is a
        // comma-separated list of day abbreviations ("Mon,Tue,Wed,Thu,Fri,Sat,Sun"
        // or any subset). Defaults to all seven days.
        public bool ScheduleEnabled;
        public string ScheduleMountTime;
        public string ScheduleUnmountTime;
        public string ScheduleDays;
        // Watch-folder upload. WatchFolderEnabled gates the FileSystemWatcher
        // and the uploader. WatchFolderPath is the local directory to monitor;
        // WatchFolderTargetDir is an optional subdir on the remote ("" means
        // root). WatchFolderMode is "move" (delete after upload) or "copy"
        // (keep local). WatchFolderQuietMs is the dwell after the last write
        // before the file is considered ready to upload (defaults to 5000ms).
        public bool WatchFolderEnabled;
        public string WatchFolderPath;
        public string WatchFolderTargetDir;
        public string WatchFolderMode;
        public int WatchFolderQuietMs;

        [ScriptIgnore] public Process MountProcess;
        [ScriptIgnore] public bool DesiredMounted;
        [ScriptIgnore] public int RemountAttempts;
        [ScriptIgnore] public DateTime RemountWindowUtc;
        [ScriptIgnore] public DateTime LastAboutRefreshUtc;
        [ScriptIgnore] public string StatusText;
        [ScriptIgnore] public string StorageText;
        [ScriptIgnore] public long StorageUsedBytes;
        [ScriptIgnore] public long StorageTotalBytes;
        [ScriptIgnore] public long StorageFreeBytes;
        [ScriptIgnore] public long ObjectCount;
        [ScriptIgnore] public string TransferQuotaText;
        [ScriptIgnore] public string SessionText;
        [ScriptIgnore] public string SpeedText;
        [ScriptIgnore] public string LastError;
        [ScriptIgnore] public int RcPort;
        [ScriptIgnore] public string LogFile;
        // Cleared on Test profile completion; written to so the diagnostics view
        // can show the user the latest preflight report without re-running it.
        [ScriptIgnore] public string LastPreflightReport;
        [ScriptIgnore] public DateTime LastPreflightUtc;
        // Transfer-completion tracking. When the live rclone stats show an
        // active transfer (transferring > 0), we capture TransferStartBytes so
        // we can report the delta when transferring returns to zero.
        [ScriptIgnore] public bool TransferActive;
        [ScriptIgnore] public long TransferStartBytes;
        // Per-profile schedule throttling. Records the day-key the mount/unmount
        // most recently fired on so a single HH:mm window doesn't re-trigger as
        // the 30-second timer ticks repeatedly through the same minute.
        [ScriptIgnore] public string LastScheduleMountKey;
        [ScriptIgnore] public string LastScheduleUnmountKey;
        // Watch-folder runtime stats. Updated by the WatchFolder worker thread
        // and read by the UI thread; no lock since these are independent
        // counters that don't drive any reactive state machine.
        [ScriptIgnore] public int WatchQueueCount;
        [ScriptIgnore] public int WatchUploadingCount;
        [ScriptIgnore] public int WatchUploadedTotal;
        [ScriptIgnore] public int WatchFailedTotal;
        [ScriptIgnore] public string WatchLastResult;
        [ScriptIgnore] public DateTime WatchLastResultUtc;

        public RemoteProfile()
        {
            Id = Guid.NewGuid().ToString("N");
            Label = "Pixeldrain";
            Provider = "pixeldrain";
            Remote = "Pixeldrain:";
            DriveLetter = "P:";
            MountMode = "network";
            AutoMount = false;
            FullCache = false;
            BandwidthLimit = "";
            ScheduleEnabled = false;
            ScheduleMountTime = "";
            ScheduleUnmountTime = "";
            ScheduleDays = "Mon,Tue,Wed,Thu,Fri,Sat,Sun";
            WatchFolderEnabled = false;
            WatchFolderPath = "";
            WatchFolderTargetDir = "";
            WatchFolderMode = "move";
            WatchFolderQuietMs = 5000;
            WatchLastResult = "";
            StatusText = "not mounted";
            ProviderCapabilities cap = ProviderCapabilities.For(Provider);
            StorageText = cap.DefaultStorageText();
            TransferQuotaText = cap.DefaultTransferQuotaText();
            StorageUsedBytes = -1;
            StorageTotalBytes = -1;
            StorageFreeBytes = -1;
            ObjectCount = -1;
            SessionText = "session not mounted";
            SpeedText = "speed not mounted";
            LastError = "";
        }
    }
}
