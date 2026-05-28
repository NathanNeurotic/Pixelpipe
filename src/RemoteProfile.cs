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

        [ScriptIgnore] public Process MountProcess;
        [ScriptIgnore] public bool DesiredMounted;
        [ScriptIgnore] public int RemountAttempts;
        [ScriptIgnore] public DateTime RemountWindowUtc;
        [ScriptIgnore] public DateTime LastAboutRefreshUtc;
        [ScriptIgnore] public string StatusText;
        [ScriptIgnore] public string StorageText;
        [ScriptIgnore] public string SessionText;
        [ScriptIgnore] public string SpeedText;
        [ScriptIgnore] public string LastError;
        [ScriptIgnore] public int RcPort;
        [ScriptIgnore] public string LogFile;

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
            StatusText = "not mounted";
            StorageText = "storage not checked";
            SessionText = "session not mounted";
            SpeedText = "speed not mounted";
            LastError = "";
        }
    }
}
