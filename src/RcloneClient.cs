using System.Collections.Generic;
using System.Diagnostics;

namespace Pixelpipe
{
    // ARCH-1 step 1 (v0.15.1, audit): first collaborator extracted from
    // the TrayContext partial. Owns the "invoke rclone, return a structured
    // result" surface. The legacy TrayContext.RunRcloneCapture wrappers
    // continue to exist and delegate here so no caller has to change — but
    // new code can take RcloneClient as a dependency directly and be unit-
    // testable without spinning up a TrayContext.
    //
    // Path provider rather than a captured string because TrayContext.rclonePath
    // mutates over the app's lifetime (first launch, "Download portable rclone"
    // button, settings-driven path changes). The Func keeps us always-current
    // without an explicit Refresh() call.
    //
    // SEC-1 fixed-stdin variant lives here too so the obscure-secret path
    // (Pixelpipe.SecretConfig) stops reaching into TrayContext's helpers.
    internal sealed class RcloneClient
    {
        private readonly System.Func<string> _exePathProvider;

        public RcloneClient(System.Func<string> exePathProvider)
        {
            _exePathProvider = exePathProvider;
        }

        public string ResolvedPath { get { return _exePathProvider == null ? "" : (_exePathProvider() ?? ""); } }

        public TrayContext.ProcessResult Run(string arguments, int timeoutMs)
        {
            return Run(arguments, timeoutMs, null);
        }

        public TrayContext.ProcessResult Run(string arguments, int timeoutMs, Dictionary<string, string> envOverrides)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ResolvedPath;
            psi.Arguments = arguments ?? "";
            if (envOverrides != null)
            {
                foreach (KeyValuePair<string, string> kv in envOverrides)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    psi.EnvironmentVariables[kv.Key] = kv.Value ?? "";
                }
            }
            return TrayContext.RunCaptureCore(psi, timeoutMs);
        }

        public TrayContext.ProcessResult RunWithStdin(string arguments, int timeoutMs, string stdinInput)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ResolvedPath;
            psi.Arguments = arguments ?? "";
            return TrayContext.RunCaptureCore(psi, timeoutMs, stdinInput);
        }
    }
}
