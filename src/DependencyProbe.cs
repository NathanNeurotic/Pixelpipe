using System;
using System.IO;
using Microsoft.Win32;

namespace Pixelpipe
{
    // ARCH-1 step 3 (v0.15.3, audit): third collaborator extracted from
    // the TrayContext partial. Owns the cached rclone / WinFsp availability
    // state plus the synchronous probes themselves.
    //
    // The TTL + UI-thread-friendly cached accessors stay where they were
    // semantically — UI callers ask "is rclone available?" and get back
    // whatever the last refresh wrote, never touching disk on the UI thread.
    // The async refresh worker (RefreshDependencyStatusAsync) stays in
    // TrayContext for now because it composes results into the setup-status
    // line; it just hands off the actual probe calls to this class.
    internal sealed class DependencyProbe
    {
        public const int CacheTtlSeconds = 30;

        private readonly Func<string> _rclonePathProvider;
        private readonly Action<string, Exception> _logIssue;
        private volatile bool _cachedRcloneAvailable;
        private volatile bool _cachedWinfspInstalled;
        private DateTime _cachedStampUtc = DateTime.MinValue;

        public DependencyProbe(Func<string> rclonePathProvider, Action<string, Exception> logIssue)
        {
            _rclonePathProvider = rclonePathProvider;
            _logIssue = logIssue;
        }

        public bool RcloneAvailable { get { return _cachedRcloneAvailable; } }
        public bool WinFspInstalled { get { return _cachedWinfspInstalled; } }
        public DateTime LastProbeUtc { get { return _cachedStampUtc; } }
        public bool IsStale { get { return (DateTime.UtcNow - _cachedStampUtc).TotalSeconds >= CacheTtlSeconds; } }

        // Called by the async refresh worker after the slow probes complete.
        public void PublishProbeResults(bool rcloneAvailable, bool winfspInstalled)
        {
            _cachedRcloneAvailable = rcloneAvailable;
            _cachedWinfspInstalled = winfspInstalled;
            _cachedStampUtc = DateTime.UtcNow;
        }

        // Synchronous rclone probe. Tries the resolved path first; if that
        // file isn't present, falls back to spawning `rclone.exe version`
        // (PATH resolution). Returns true if either path responds.
        public bool ProbeRcloneSync(Func<string, int, string> runProcessCapture)
        {
            try
            {
                string resolved = _rclonePathProvider == null ? null : _rclonePathProvider();
                if (!String.IsNullOrEmpty(resolved) && File.Exists(resolved)) return true;
                if (runProcessCapture == null) return false;
                string version = runProcessCapture("rclone.exe", 3000);
                return !String.IsNullOrEmpty(version) && version.IndexOf("rclone", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception ex) { if (_logIssue != null) _logIssue("dep probe rclone", ex); return false; }
        }

        // Synchronous WinFsp probe. Two on-disk dll locations + two registry
        // keys; any positive signal is enough.
        public bool ProbeWinFspSync()
        {
            try
            {
                string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (File.Exists(Path.Combine(pf86, "WinFsp", "bin", "winfsp-x64.dll"))) return true;
                if (File.Exists(Path.Combine(pf, "WinFsp", "bin", "winfsp-x64.dll"))) return true;
                using (RegistryKey k1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinFsp")) { if (k1 != null) return true; }
                using (RegistryKey k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinFsp")) { if (k2 != null) return true; }
            }
            catch (Exception ex) { if (_logIssue != null) _logIssue("dep probe winfsp", ex); }
            return false;
        }
    }
}
