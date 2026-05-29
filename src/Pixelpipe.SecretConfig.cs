using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Pixelpipe
{
    // SEC-1 (v0.13.0): provider wizards used to call
    //   rclone config create NAME TYPE access_key_id AKIA... secret_access_key hunter2... --non-interactive
    // and during the few seconds that process was alive any other user-level
    // process could read the full command line via Win32_Process.CommandLine
    // (rclone obscures values at rest in rclone.conf, but not on argv).
    // Pixelpipe's own orphan scan proves this exposure exists.
    //
    // This helper writes directly to rclone.conf, obscuring secret values via
    // `rclone obscure -` over stdin so plaintext never appears on argv. Values
    // land in rclone.conf in the same form rclone would have written them.
    internal sealed partial class TrayContext
    {
        // Field keys that should always be obscured before writing to
        // rclone.conf. Matches rclone's own IsPassword convention.
        private static readonly HashSet<string> SecretFieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pass", "password", "api_key", "secret_access_key", "session_token",
            "client_secret", "key", "auth_token", "service_account_credentials",
            "service_account_file"
        };

        internal static bool IsSecretField(string fieldKey)
        {
            if (String.IsNullOrEmpty(fieldKey)) return false;
            return SecretFieldKeys.Contains(fieldKey);
        }

        // Writes (or replaces) the named remote in rclone.conf with the given
        // type and fields. Secret fields are obscured via `rclone obscure -`
        // (plaintext piped over stdin, never on argv). Returns null on success,
        // an error string on failure.
        internal string WriteRemoteToRcloneConfig(string remoteName, string rcloneType, List<KeyValuePair<string, string>> fields)
        {
            try
            {
                string configPath = FindRcloneConfigPath();
                if (String.IsNullOrEmpty(configPath))
                {
                    return "rclone config path not found";
                }
                // Make sure the parent directory exists; on a clean install
                // rclone.conf may not yet have been written.
                string parent = Path.GetDirectoryName(configPath);
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                // Obscure all secret fields up front so the new section can
                // be built in one pass. If obscure fails for any field we
                // bail without touching the config file.
                List<KeyValuePair<string, string>> sanitized = new List<KeyValuePair<string, string>>();
                if (fields != null)
                {
                    for (int i = 0; i < fields.Count; i++)
                    {
                        KeyValuePair<string, string> kv = fields[i];
                        if (String.IsNullOrEmpty(kv.Key)) continue;
                        if (IsSecretField(kv.Key) && !String.IsNullOrEmpty(kv.Value))
                        {
                            string obscured;
                            string err = ObscureSecretViaStdin(kv.Value, out obscured);
                            if (err != null) return "obscure '" + kv.Key + "' failed: " + err;
                            sanitized.Add(new KeyValuePair<string, string>(kv.Key, obscured));
                        }
                        else
                        {
                            sanitized.Add(kv);
                        }
                    }
                }

                string existing = File.Exists(configPath) ? File.ReadAllText(configPath, Encoding.UTF8) : "";
                string merged = MergeRcloneConfigSection(existing, remoteName, rcloneType, sanitized);
                WriteAllTextAtomic(configPath, merged, Encoding.UTF8);
                LogUiInfo("rclone config", "wrote section [" + remoteName + "] type=" + rcloneType + " (secrets piped via stdin)");
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // Pure helper: inserts or replaces the [name] section in an existing
        // rclone.conf body. Preserves the rest of the file verbatim. Tested
        // via TestRunner.
        internal static string MergeRcloneConfigSection(string existing, string remoteName, string rcloneType, List<KeyValuePair<string, string>> fields)
        {
            if (existing == null) existing = "";
            // Normalize line endings for the parser. We write the result with
            // CRLF to match what rclone itself produces on Windows.
            string normalized = existing.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');
            StringBuilder kept = new StringBuilder();
            bool inTargetSection = false;
            string targetHeader = "[" + remoteName + "]";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    if (String.Equals(trimmed, targetHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        inTargetSection = true;
                        continue; // drop the old header
                    }
                    else
                    {
                        inTargetSection = false;
                    }
                }
                if (inTargetSection) continue; // skip old section body
                kept.Append(line);
                if (i < lines.Length - 1) kept.Append('\n');
            }
            string cleaned = kept.ToString().TrimEnd('\n');
            StringBuilder result = new StringBuilder();
            if (cleaned.Length > 0)
            {
                result.Append(cleaned);
                result.Append("\n\n");
            }
            result.Append('[').Append(remoteName).Append("]\n");
            result.Append("type = ").Append(rcloneType).Append('\n');
            if (fields != null)
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    KeyValuePair<string, string> kv = fields[i];
                    if (String.IsNullOrEmpty(kv.Key)) continue;
                    result.Append(kv.Key).Append(" = ").Append(kv.Value ?? "").Append('\n');
                }
            }
            return result.ToString().Replace("\n", "\r\n");
        }

        // Pipes `value` to `rclone obscure -` via stdin and captures the
        // obscured form. rclone's obscure is a known XOR cipher and is
        // reversible — the security gain is removing the plaintext from
        // argv during the brief window of the create operation, not at-rest
        // protection (which would require rclone config encryption).
        private string ObscureSecretViaStdin(string plaintext, out string obscured)
        {
            obscured = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = rclonePath;
                psi.Arguments = "obscure -";
                ProcessResult res = RunCaptureCore(psi, 5000, plaintext);
                if (!res.Succeeded)
                {
                    return res.TimedOut ? "obscure timed out"
                        : (!String.IsNullOrEmpty(res.LaunchError) ? res.LaunchError
                        : "rclone obscure exit " + res.ExitCode);
                }
                obscured = (res.StdOut ?? "").Trim();
                if (String.IsNullOrEmpty(obscured)) return "rclone obscure returned empty result";
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // Locates the active rclone.conf. Honours user environment overrides
        // by asking rclone itself; falls back to %APPDATA%\rclone\rclone.conf
        // which is rclone's default on Windows.
        private string FindRcloneConfigPath()
        {
            try
            {
                ProcessResult r = RunRcloneCaptureResult("config file", 5000);
                if (r.Succeeded)
                {
                    string output = (r.StdOut ?? "").Trim();
                    // `rclone config file` prints e.g.
                    //   Configuration file is stored at:
                    //   C:\Users\foo\AppData\Roaming\rclone\rclone.conf
                    string[] lines = output.Replace("\r", "").Split('\n');
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        string trimmed = lines[i].Trim();
                        if (trimmed.Length == 0) continue;
                        if (trimmed.IndexOf(":\\") >= 0 || trimmed.StartsWith("/")) return trimmed;
                    }
                }
            }
            catch (Exception ex) { LogUiIssue("rclone config path", ex); }
            // Default fallback: rclone's standard Windows location.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "rclone", "rclone.conf");
        }
    }
}
