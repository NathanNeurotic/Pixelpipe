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
