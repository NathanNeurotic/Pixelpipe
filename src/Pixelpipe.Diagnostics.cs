using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        // Open the main window on its Diagnostics tab. The legacy modal-form version
        // used hardcoded pixel positions that clipped buttons and the verbose-logging
        // caption at the user's font/DPI; the tabbed Diagnostics view inside the main
        // window has the same actions plus auto-refresh and proper layout.
        private void ShowDiagnosticsWindow()
        {
            ShowMainWindow("Diagnostics");
        }

        private string BuildDiagnosticsText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Pixelpipe diagnostics");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Running elevated: " + IsAdministrator());
            sb.AppendLine("rclone available: " + RcloneAvailable());
            sb.AppendLine("rclone path: " + rclonePath);
            sb.AppendLine("WinFsp installed: " + WinFspInstalled());
            RemoteProfile[] snapshot = SnapshotProfiles();
            sb.AppendLine("configured profiles: " + snapshot.Length.ToString());
            sb.AppendLine("any remote configured: " + AnyRemoteConfigured());
            sb.AppendLine("bandwidth: " + DisplayLimit(selectedBandwidth));
            sb.AppendLine("PixelDrain API key configured: " + ApiKeyConfigured());
            sb.AppendLine("transfer quota: " + transferQuotaText);
            sb.AppendLine("settings file: " + settingsFile);
            sb.AppendLine("log dir: " + logDir);
            sb.AppendLine("ui log: " + uiLogFile);
            sb.AppendLine();
            for (int i = 0; i < snapshot.Length; i++)
            {
                RemoteProfile p = snapshot[i];
                sb.AppendLine("Profile " + (i + 1).ToString() + ": " + p.Label);
                sb.AppendLine("  provider: " + p.Provider);
                sb.AppendLine("  remote: " + p.Remote);
                sb.AppendLine("  drive: " + p.DriveLetter);
                sb.AppendLine("  mode: " + p.MountMode);
                sb.AppendLine("  auto-mount: " + p.AutoMount);
                sb.AppendLine("  remote configured: " + RemoteConfigured(p));
                sb.AppendLine("  mounted by Pixelpipe: " + IsMounted(p));
                sb.AppendLine("  rc: 127.0.0.1:" + p.RcPort.ToString());
                sb.AppendLine("  status: " + p.StatusText);
                sb.AppendLine("  storage: " + p.StorageText);
                sb.AppendLine("  session: " + p.SessionText);
                sb.AppendLine("  speed: " + p.SpeedText);
                sb.AppendLine("  log: " + p.LogFile);
                if (!String.IsNullOrWhiteSpace(p.LastError)) sb.AppendLine("  last error: " + p.LastError);
                sb.AppendLine("  log tail:");
                sb.AppendLine(TailLog(p, 2000));
                sb.AppendLine();
            }
            sb.AppendLine("Pixelpipe UI log tail:");
            sb.AppendLine(TailUiLog(2000));
            return sb.ToString();
        }

        private void CopyDiagnostics()
        {
            try
            {
                Clipboard.SetText(BuildDiagnosticsText());
                ShowBalloon("Diagnostics copied to clipboard.");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void OpenSettingsFile()
        {
            try
            {
                Directory.CreateDirectory(settingsDir);
                if (!File.Exists(settingsFile)) SaveProfiles();
                // ShellExecute opens with the user's default .json handler instead of
                // hard-coding notepad.
                ProcessStartInfo psi = new ProcessStartInfo(settingsFile);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void OpenLogFolder()
        {
            try { Directory.CreateDirectory(logDir); Process.Start(logDir); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private string TailLog(RemoteProfile p, int maxChars)
        {
            try
            {
                string file = p == null ? "" : p.LogFile;
                if (!File.Exists(file)) return "No log file exists yet.";
                string s = File.ReadAllText(file);
                if (s.Length <= maxChars) return s;
                return s.Substring(s.Length - maxChars);
            }
            catch (Exception ex) { return ex.Message; }
        }

        private string TailUiLog(int maxChars)
        {
            try
            {
                if (!File.Exists(uiLogFile)) return "No UI helper log entries yet.";
                string s = File.ReadAllText(uiLogFile);
                if (s.Length <= maxChars) return s;
                return s.Substring(s.Length - maxChars);
            }
            catch (Exception ex) { return ex.Message; }
        }

        private void CheckForUpdates()
        {
            try { Process.Start("https://github.com/NathanNeurotic/Pixelpipe/releases/latest"); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe update check", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private bool StartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    object value = key.GetValue(AppName);
                    if (value == null) return false;
                    return value.ToString().IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        private void ToggleStartup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (StartupEnabled())
                    {
                        key.DeleteValue(AppName, false);
                        ShowBalloon("Startup auto-mount disabled.");
                    }
                    else
                    {
                        key.SetValue(AppName, QuoteArg(Application.ExecutablePath) + " /automount", RegistryValueKind.String);
                        ShowBalloon("Startup auto-mount enabled.");
                    }
                }
                RebuildMenu();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe startup setting", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
