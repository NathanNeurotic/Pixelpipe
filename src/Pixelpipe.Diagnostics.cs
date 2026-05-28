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
        private void ShowDiagnosticsWindow()
        {
            Form form = new Form();
            form.Text = "Pixelpipe diagnostics / repair";
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Width = 820;
            form.Height = 600;
            form.BackColor = Color.FromArgb(18, 22, 28);
            form.ForeColor = Color.WhiteSmoke;
            TextBox box = new TextBox();
            box.Multiline = true; box.ReadOnly = true; box.ScrollBars = ScrollBars.Vertical; box.Font = new Font("Consolas", 9f);
            box.Left = 12; box.Top = 12; box.Width = 780; box.Height = 380; box.Text = BuildDiagnosticsText();
            CheckBox verbose = new CheckBox();
            verbose.Text = "Verbose logging (writes [debug] lines for menu placement and refresh)";
            verbose.Left = 12; verbose.Top = 510; verbose.Width = 600; verbose.ForeColor = Color.WhiteSmoke;
            verbose.Checked = verboseLogging;
            verbose.CheckedChanged += delegate
            {
                verboseLogging = verbose.Checked;
                SaveSetting("VerboseLogging", verboseLogging ? "1" : "0");
            };
            Button refresh = new Button(); refresh.Text = "Refresh"; refresh.Left = 12; refresh.Top = 410; refresh.Width = 90; refresh.Click += delegate { box.Text = BuildDiagnosticsText(); };
            Button copy = new Button(); copy.Text = "Copy"; copy.Left = 110; copy.Top = 410; copy.Width = 90; copy.Click += delegate { Clipboard.SetText(box.Text); };
            Button installRclone = new Button(); installRclone.Text = "Install rclone"; installRclone.Left = 208; installRclone.Top = 410; installRclone.Width = 110; installRclone.Click += delegate { DownloadRclonePortableWithUi(); box.Text = BuildDiagnosticsText(); };
            Button installWinFsp = new Button(); installWinFsp.Text = "Install WinFsp"; installWinFsp.Left = 326; installWinFsp.Top = 410; installWinFsp.Width = 110; installWinFsp.Click += delegate { InstallWinFspWithWinget(); };
            Button configRemote = new Button(); configRemote.Text = "rclone config"; configRemote.Left = 444; configRemote.Top = 410; configRemote.Width = 110; configRemote.Click += delegate { OpenRcloneConfigTerminal(); };
            Button cleanup = new Button(); cleanup.Text = "Clear stale primary drive"; cleanup.Left = 562; cleanup.Top = 410; cleanup.Width = 150; cleanup.Click += delegate { CleanStaleDriveMappings(GetPrimaryProfile(), true); box.Text = BuildDiagnosticsText(); };
            Button restart = new Button(); restart.Text = "Restart primary"; restart.Left = 12; restart.Top = 450; restart.Width = 120; restart.Click += delegate { RemoteProfile p = GetPrimaryProfile(); bool full = p.FullCache; UnmountProfile(p, true); MountProfile(p, full); };
            Button logs = new Button(); logs.Text = "Open logs"; logs.Left = 140; logs.Top = 450; logs.Width = 100; logs.Click += delegate { OpenLogFolder(); };
            Button settings = new Button(); settings.Text = "Open settings"; settings.Left = 248; settings.Top = 450; settings.Width = 110; settings.Click += delegate { OpenSettingsFile(); };
            Button close = new Button(); close.Text = "Close"; close.Left = 702; close.Top = 510; close.Width = 90; close.Click += delegate { form.Close(); };
            form.Controls.Add(box); form.Controls.Add(refresh); form.Controls.Add(copy); form.Controls.Add(installRclone); form.Controls.Add(installWinFsp); form.Controls.Add(configRemote); form.Controls.Add(cleanup); form.Controls.Add(restart); form.Controls.Add(logs); form.Controls.Add(settings); form.Controls.Add(verbose); form.Controls.Add(close);

            // Auto-refresh while the dialog is open so live values update.
            System.Windows.Forms.Timer diagTimer = new System.Windows.Forms.Timer();
            diagTimer.Interval = 5000;
            diagTimer.Tick += delegate
            {
                if (!box.IsDisposed) box.Text = BuildDiagnosticsText();
            };
            diagTimer.Start();
            form.FormClosed += delegate
            {
                diagTimer.Stop();
                diagTimer.Dispose();
                form.Dispose();
            };
            form.Show();
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
            sb.AppendLine("configured profiles: " + profiles.Count.ToString());
            sb.AppendLine("any remote configured: " + AnyRemoteConfigured());
            sb.AppendLine("bandwidth: " + DisplayLimit(selectedBandwidth));
            sb.AppendLine("PixelDrain API key configured: " + ApiKeyConfigured());
            sb.AppendLine("transfer quota: " + transferQuotaText);
            sb.AppendLine("settings file: " + settingsFile);
            sb.AppendLine("log dir: " + logDir);
            sb.AppendLine("ui log: " + uiLogFile);
            sb.AppendLine();
            for (int i = 0; i < profiles.Count; i++)
            {
                RemoteProfile p = profiles[i];
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
                        key.SetValue(AppName, Quote(Application.ExecutablePath) + " /automount", RegistryValueKind.String);
                        ShowBalloon("Startup auto-mount enabled.");
                    }
                }
                RebuildMenu();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pixelpipe startup setting", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
