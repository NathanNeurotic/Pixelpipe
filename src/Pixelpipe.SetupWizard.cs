using System;
using System.Drawing;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        // Multi-step wizard replacing the old MessageBox-chain first-launch path.
        // Returns true if the user completed the wizard (any combination of steps).
        // Returns false if they cancelled at the welcome screen.
        private bool ShowSetupWizard(bool manualReRun)
        {
            using (SetupWizard wizard = new SetupWizard(this, manualReRun))
            {
                DialogResult result = wizard.ShowDialog();
                return result == DialogResult.OK;
            }
        }

        private sealed class SetupWizard : Form
        {
            private readonly TrayContext owner;
            private readonly bool manualReRun;
            private int step;
            private const int StepCount = 5;

            private Label header;
            private Label body;
            private Label depStatus;
            private TextBox apiKeyBox;
            private CheckBox dontAskAgain;
            private Button backBtn;
            private Button nextBtn;
            private Button skipBtn;
            private Button cancelBtn;
            private Button installRcloneBtn;
            private Button winfspBtn;
            private Button configureRemoteBtn;

            public SetupWizard(TrayContext owner, bool manualReRun)
            {
                this.owner = owner;
                this.manualReRun = manualReRun;
                Text = "Pixelpipe setup";
                StartPosition = FormStartPosition.CenterScreen;
                Width = 560; Height = 380;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                BackColor = Color.FromArgb(18, 22, 28);
                ForeColor = Color.WhiteSmoke;

                header = new Label();
                header.Left = 16; header.Top = 16; header.Width = 520; header.Height = 28;
                header.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
                header.ForeColor = Color.WhiteSmoke;

                body = new Label();
                body.Left = 16; body.Top = 52; body.Width = 520; body.Height = 100;
                body.ForeColor = Color.WhiteSmoke;

                depStatus = new Label();
                depStatus.Left = 16; depStatus.Top = 156; depStatus.Width = 520; depStatus.Height = 40;
                depStatus.ForeColor = Color.FromArgb(180, 200, 220);

                installRcloneBtn = MakeButton("Download portable rclone", 220, 16, 200);
                installRcloneBtn.Visible = false;
                installRcloneBtn.Click += delegate { owner.DownloadRclonePortableWithUi(); RefreshDeps(); };

                winfspBtn = MakeButton("Install WinFsp (winget)", 220, 16, 200);
                winfspBtn.Visible = false;
                winfspBtn.Click += delegate { owner.InstallWinFspWithWinget(); RefreshDeps(); };

                configureRemoteBtn = MakeButton("Configure Pixeldrain remote", 220, 16, 240);
                configureRemoteBtn.Visible = false;
                configureRemoteBtn.Click += delegate { owner.ConfigurePixeldrainRemoteFromPrompt(owner.GetPrimaryProfile()); RefreshDeps(); };

                apiKeyBox = new TextBox();
                apiKeyBox.Left = 16; apiKeyBox.Top = 220; apiKeyBox.Width = 520;
                apiKeyBox.UseSystemPasswordChar = true;
                apiKeyBox.Visible = false;
                apiKeyBox.Text = owner.LoadApiKey();

                dontAskAgain = new CheckBox();
                dontAskAgain.Left = 16; dontAskAgain.Top = 260; dontAskAgain.Width = 520;
                dontAskAgain.Text = "Don't show this wizard again if dependencies are missing";
                dontAskAgain.ForeColor = Color.WhiteSmoke;
                dontAskAgain.Visible = false;

                backBtn = MakeButton("Back", 280, 300, 80);
                backBtn.Click += delegate { step--; Render(); };
                nextBtn = MakeButton("Next", 366, 300, 80);
                nextBtn.Click += delegate { OnNext(); };
                skipBtn = MakeButton("Skip", 194, 300, 80);
                skipBtn.Click += delegate { step++; Render(); };
                cancelBtn = MakeButton("Cancel", 452, 300, 80);
                cancelBtn.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

                Controls.Add(header);
                Controls.Add(body);
                Controls.Add(depStatus);
                Controls.Add(installRcloneBtn);
                Controls.Add(winfspBtn);
                Controls.Add(configureRemoteBtn);
                Controls.Add(apiKeyBox);
                Controls.Add(dontAskAgain);
                Controls.Add(backBtn);
                Controls.Add(nextBtn);
                Controls.Add(skipBtn);
                Controls.Add(cancelBtn);

                step = 0;
                Render();
            }

            private void OnNext()
            {
                if (step == 4)
                {
                    // Last step: save API key if filled, persist skip preference, finish.
                    string apiKey = (apiKeyBox.Text ?? "").Trim();
                    if (apiKey.Length > 0) owner.SaveApiKey(apiKey);
                    owner.SaveSetting("SkipMissingDepWizard", dontAskAgain.Checked ? "1" : "0");
                    owner.SaveSetting("FirstLaunchSetupDone", "1");
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
                step++;
                Render();
            }

            private void Render()
            {
                if (step < 0) step = 0;
                if (step > StepCount - 1) step = StepCount - 1;

                installRcloneBtn.Visible = false;
                winfspBtn.Visible = false;
                configureRemoteBtn.Visible = false;
                apiKeyBox.Visible = false;
                dontAskAgain.Visible = false;
                depStatus.Visible = true;

                backBtn.Visible = step > 0 && step < StepCount - 1;
                skipBtn.Visible = step > 0 && step < StepCount - 1;
                nextBtn.Text = step == StepCount - 1 ? "Finish" : "Next";

                switch (step)
                {
                    case 0:
                        header.Text = manualReRun ? "Re-run Pixelpipe setup" : "Welcome to Pixelpipe";
                        body.Text = "Pixelpipe mounts Pixeldrain and other rclone remotes as Windows drives. " +
                                    "The next few steps will check the basics: rclone, WinFsp, and an optional PixelDrain API key for quota display.\r\n\r\n" +
                                    "Each step has a Skip button if you want to handle something later.";
                        depStatus.Visible = false;
                        nextBtn.Text = "Get started";
                        break;
                    case 1:
                        header.Text = "Step 1 of 4 — rclone";
                        body.Text = "Pixelpipe uses rclone to talk to cloud backends. If you don't already have it, we can drop a portable copy under your user profile.";
                        installRcloneBtn.Visible = !owner.RcloneAvailable();
                        RefreshDeps();
                        break;
                    case 2:
                        header.Text = "Step 2 of 4 — WinFsp";
                        body.Text = "rclone mount needs WinFsp to expose a cloud remote as a Windows drive. Install via winget (an elevation prompt will appear).";
                        winfspBtn.Visible = !owner.WinFspInstalled();
                        RefreshDeps();
                        break;
                    case 3:
                        header.Text = "Step 3 of 4 — rclone remote";
                        body.Text = "Configure your first remote so you have something to mount. The Pixeldrain helper does everything in one prompt; for other providers use rclone config.";
                        configureRemoteBtn.Visible = !owner.AnyRemoteConfigured();
                        RefreshDeps();
                        break;
                    case 4:
                        header.Text = "Step 4 of 4 — Optional PixelDrain API key";
                        body.Text = "If you have a Pixeldrain account, paste your API key for monthly transfer quota in the tray. It's encrypted with Windows DPAPI for your user only. Leave blank to skip.";
                        depStatus.Visible = false;
                        apiKeyBox.Visible = true;
                        apiKeyBox.Text = owner.LoadApiKey();
                        dontAskAgain.Visible = true;
                        dontAskAgain.Checked = String.Equals(owner.LoadSetting("SkipMissingDepWizard", "0"), "1", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            private void RefreshDeps()
            {
                bool rclone = owner.RcloneAvailable();
                bool winfsp = owner.WinFspInstalled();
                bool remote = rclone && owner.AnyRemoteConfigured();
                depStatus.Text = "Current state:\r\n" +
                                 "  rclone: " + (rclone ? "found" : "missing") + "\r\n" +
                                 "  WinFsp: " + (winfsp ? "found" : "missing") + "\r\n" +
                                 "  rclone remote: " + (remote ? "configured" : "missing");
            }

            private static Button MakeButton(string text, int left, int top, int width)
            {
                Button b = new Button();
                b.Text = text;
                b.Left = left; b.Top = top; b.Width = width; b.Height = 28;
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Color.FromArgb(40, 44, 52);
                b.ForeColor = Color.WhiteSmoke;
                b.FlatAppearance.BorderColor = Color.FromArgb(70, 80, 92);
                return b;
            }
        }
    }
}
