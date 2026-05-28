using System;
using System.Drawing;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        // Multi-step wizard replacing the old MessageBox-chain first-launch path.
        // Returns true if the user completed the wizard (any combination of steps).
        // Returns false if they cancelled.
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
            // Use the same palette as the main window / quick controls / profile
            // cards. Previous SetupWizard-specific values differed by a handful of
            // RGB points which read as inconsistent dark theming side-by-side.
            private static Color BgColor { get { return WindowTheme.BgColor; } }
            private static Color FgColor { get { return WindowTheme.FgColor; } }
            private static Color MutedColor { get { return WindowTheme.MutedColor; } }
            private static Color ButtonBg { get { return WindowTheme.ButtonBg; } }
            private static Color ButtonBorder { get { return WindowTheme.ButtonBorder; } }

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
            private TableLayoutPanel actionsRow;

            public SetupWizard(TrayContext owner, bool manualReRun)
            {
                this.owner = owner;
                this.manualReRun = manualReRun;
                Text = "Pixelpipe setup";
                StartPosition = FormStartPosition.CenterScreen;
                Width = 620;
                Height = 460;
                MinimumSize = new Size(560, 400);
                FormBorderStyle = FormBorderStyle.Sizable;
                MinimizeBox = false;
                MaximizeBox = false;
                BackColor = BgColor;
                ForeColor = FgColor;
                Font = new Font("Segoe UI", 9.25f);
                AutoScaleMode = AutoScaleMode.Dpi;

                // Outer layout: header / body (fills) / footer with buttons
                TableLayoutPanel root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.ColumnCount = 1;
                root.RowCount = 3;
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Padding = new Padding(16);
                root.BackColor = BgColor;

                header = new Label();
                header.AutoSize = true;
                header.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
                header.ForeColor = FgColor;
                header.Margin = new Padding(0, 0, 0, 10);

                // Body region: a vertical FlowLayoutPanel that auto-sizes its contents
                FlowLayoutPanel bodyPanel = new FlowLayoutPanel();
                bodyPanel.Dock = DockStyle.Fill;
                bodyPanel.FlowDirection = FlowDirection.TopDown;
                bodyPanel.WrapContents = false;
                bodyPanel.AutoScroll = true;
                bodyPanel.BackColor = BgColor;

                body = new Label();
                body.AutoSize = true;
                body.MaximumSize = new Size(560, 0);
                body.ForeColor = FgColor;
                body.Margin = new Padding(0, 0, 0, 12);

                depStatus = new Label();
                depStatus.AutoSize = true;
                depStatus.ForeColor = MutedColor;
                depStatus.Margin = new Padding(0, 0, 0, 12);

                actionsRow = new TableLayoutPanel();
                actionsRow.AutoSize = true;
                actionsRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                actionsRow.ColumnCount = 1;
                actionsRow.RowCount = 1;
                actionsRow.BackColor = BgColor;
                actionsRow.Margin = new Padding(0, 0, 0, 8);

                installRcloneBtn = MakeButton("Download portable rclone");
                installRcloneBtn.Click += delegate { owner.DownloadRclonePortableWithUi(); RefreshDeps(); };

                winfspBtn = MakeButton("Install WinFsp (winget)");
                winfspBtn.Click += delegate { owner.InstallWinFspWithWinget(); RefreshDeps(); };

                configureRemoteBtn = MakeButton("Configure Pixeldrain remote");
                configureRemoteBtn.Click += delegate { owner.ConfigurePixeldrainRemoteFromPrompt(owner.GetPrimaryProfile()); RefreshDeps(); };

                apiKeyBox = new TextBox();
                apiKeyBox.UseSystemPasswordChar = true;
                apiKeyBox.Width = 540;
                apiKeyBox.Margin = new Padding(0, 0, 0, 8);
                apiKeyBox.BackColor = WindowTheme.InputBg;
                apiKeyBox.ForeColor = FgColor;
                apiKeyBox.BorderStyle = BorderStyle.FixedSingle;

                dontAskAgain = new CheckBox();
                dontAskAgain.AutoSize = true;
                dontAskAgain.Text = "Don't show this wizard again if dependencies are missing";
                dontAskAgain.ForeColor = FgColor;
                dontAskAgain.Margin = new Padding(0, 0, 0, 4);

                bodyPanel.Controls.Add(body);
                bodyPanel.Controls.Add(depStatus);
                bodyPanel.Controls.Add(actionsRow);
                bodyPanel.Controls.Add(apiKeyBox);
                bodyPanel.Controls.Add(dontAskAgain);

                // Footer: cancel on the left, skip/back/next on the right
                TableLayoutPanel footer = new TableLayoutPanel();
                footer.AutoSize = true;
                footer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                footer.ColumnCount = 2;
                footer.RowCount = 1;
                footer.Dock = DockStyle.Top;
                footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                footer.BackColor = BgColor;
                footer.Margin = new Padding(0, 12, 0, 0);

                cancelBtn = MakeButton("Cancel");
                cancelBtn.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

                FlowLayoutPanel rightButtons = new FlowLayoutPanel();
                rightButtons.AutoSize = true;
                rightButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                rightButtons.FlowDirection = FlowDirection.RightToLeft;
                rightButtons.Dock = DockStyle.Fill;

                nextBtn = MakeButton("Next");
                nextBtn.Click += delegate { OnNext(); };

                backBtn = MakeButton("Back");
                backBtn.Click += delegate { step--; Render(); };

                skipBtn = MakeButton("Skip");
                skipBtn.Click += delegate { step++; Render(); };

                rightButtons.Controls.Add(nextBtn);
                rightButtons.Controls.Add(backBtn);
                rightButtons.Controls.Add(skipBtn);

                footer.Controls.Add(cancelBtn, 0, 0);
                footer.Controls.Add(rightButtons, 1, 0);

                root.Controls.Add(header, 0, 0);
                root.Controls.Add(bodyPanel, 0, 1);
                root.Controls.Add(footer, 0, 2);

                Controls.Add(root);

                step = 0;
                Render();
            }

            private void OnNext()
            {
                if (step == StepCount - 1)
                {
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

                actionsRow.Controls.Clear();
                actionsRow.Visible = false;
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
                        if (!owner.RcloneAvailable())
                        {
                            actionsRow.Visible = true;
                            actionsRow.Controls.Add(installRcloneBtn);
                        }
                        RefreshDeps();
                        break;
                    case 2:
                        header.Text = "Step 2 of 4 — WinFsp";
                        body.Text = "rclone mount needs WinFsp to expose a cloud remote as a Windows drive. Install via winget (an elevation prompt will appear).";
                        if (!owner.WinFspInstalled())
                        {
                            actionsRow.Visible = true;
                            actionsRow.Controls.Add(winfspBtn);
                        }
                        RefreshDeps();
                        break;
                    case 3:
                        header.Text = "Step 3 of 4 — rclone remote";
                        body.Text = "Configure your first remote so you have something to mount. The Pixeldrain helper does everything in one prompt; for other providers use rclone config.";
                        if (!owner.AnyRemoteConfigured())
                        {
                            actionsRow.Visible = true;
                            actionsRow.Controls.Add(configureRemoteBtn);
                        }
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

            private static Button MakeButton(string text)
            {
                Button b = new Button();
                b.Text = text;
                b.AutoSize = true;
                b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                b.MinimumSize = new Size(0, 30);
                b.Padding = new Padding(12, 4, 12, 4);
                b.Margin = new Padding(4, 0, 4, 0);
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = ButtonBg;
                b.ForeColor = FgColor;
                b.FlatAppearance.BorderColor = ButtonBorder;
                b.UseVisualStyleBackColor = false;
                return b;
            }
        }
    }
}
