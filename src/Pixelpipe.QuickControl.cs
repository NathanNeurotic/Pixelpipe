using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        private QuickControlWindow quickWindow;

        private void ShowQuickControl()
        {
            if (quickWindow != null && !quickWindow.IsDisposed)
            {
                if (quickWindow.WindowState == FormWindowState.Minimized) quickWindow.WindowState = FormWindowState.Normal;
                quickWindow.Activate();
                quickWindow.BringToFront();
                return;
            }
            quickWindow = new QuickControlWindow(this);
            quickWindow.FormClosed += delegate { quickWindow = null; };
            quickWindow.Show();
        }

        private void UpdateQuickControlLiveState()
        {
            if (quickWindow != null && !quickWindow.IsDisposed) quickWindow.ApplyLiveState();
        }

        private sealed class QuickControlWindow : Form
        {
            private static readonly Color BgColor = Color.FromArgb(18, 22, 28);
            private static readonly Color FgColor = Color.WhiteSmoke;
            private static readonly Color MutedColor = Color.FromArgb(160, 170, 184);
            private static readonly Color AccentColor = Color.FromArgb(110, 200, 255);

            private readonly TrayContext owner;
            private Label mountSummary;
            private Label aggregateSpeed;
            private Label aggregateTraffic;
            private ComboBox bandwidthCombo;
            private FlowLayoutPanel profilesPanel;
            private readonly List<Label> perProfileLabels = new List<Label>();
            private readonly string[] bwChoices = new string[] { "off", "512K", "1M", "5M", "10M", "25M", "50M", "100M", "250M" };

            public QuickControlWindow(TrayContext owner)
            {
                this.owner = owner;
                Text = "Pixelpipe quick controls";
                StartPosition = FormStartPosition.Manual;
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - 380, Screen.PrimaryScreen.WorkingArea.Bottom - 360);
                Width = 360;
                Height = 320;
                MinimumSize = new Size(300, 260);
                FormBorderStyle = FormBorderStyle.SizableToolWindow;
                TopMost = true;
                BackColor = BgColor;
                ForeColor = FgColor;
                Font = new Font("Segoe UI", 9.25f);
                AutoScaleMode = AutoScaleMode.Dpi;

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.ColumnCount = 2;
                layout.RowCount = 5;
                layout.BackColor = BgColor;
                layout.Padding = new Padding(10);

                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // mount summary
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // big speed
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // traffic
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // bandwidth row
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // per-profile list

                mountSummary = new Label();
                mountSummary.AutoSize = true;
                mountSummary.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                mountSummary.ForeColor = FgColor;
                mountSummary.Margin = new Padding(0, 0, 0, 4);

                aggregateSpeed = new Label();
                aggregateSpeed.AutoSize = true;
                aggregateSpeed.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
                aggregateSpeed.ForeColor = AccentColor;
                aggregateSpeed.Margin = new Padding(0, 0, 0, 2);

                aggregateTraffic = new Label();
                aggregateTraffic.AutoSize = true;
                aggregateTraffic.ForeColor = MutedColor;
                aggregateTraffic.Margin = new Padding(0, 0, 0, 8);

                Label bwL = new Label();
                bwL.AutoSize = true;
                bwL.Text = "Bandwidth:";
                bwL.ForeColor = FgColor;
                bwL.Margin = new Padding(0, 6, 8, 0);

                bandwidthCombo = new ComboBox();
                bandwidthCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                bandwidthCombo.BackColor = Color.FromArgb(14, 18, 24);
                bandwidthCombo.ForeColor = FgColor;
                bandwidthCombo.Dock = DockStyle.Fill;
                bandwidthCombo.Margin = new Padding(0, 2, 0, 8);
                for (int i = 0; i < bwChoices.Length; i++)
                {
                    bandwidthCombo.Items.Add(bwChoices[i] == "off" ? "Unlimited" : bwChoices[i] + "/s");
                }
                bandwidthCombo.SelectedIndexChanged += delegate
                {
                    int idx = bandwidthCombo.SelectedIndex;
                    if (idx < 0) return;
                    string val = bwChoices[idx];
                    if (!String.Equals(val, owner.selectedBandwidth, StringComparison.OrdinalIgnoreCase))
                    {
                        owner.SetBandwidth(val);
                    }
                };

                profilesPanel = new FlowLayoutPanel();
                profilesPanel.Dock = DockStyle.Fill;
                profilesPanel.FlowDirection = FlowDirection.TopDown;
                profilesPanel.WrapContents = false;
                profilesPanel.AutoScroll = true;
                profilesPanel.BackColor = BgColor;

                layout.Controls.Add(mountSummary, 0, 0);
                layout.SetColumnSpan(mountSummary, 2);
                layout.Controls.Add(aggregateSpeed, 0, 1);
                layout.SetColumnSpan(aggregateSpeed, 2);
                layout.Controls.Add(aggregateTraffic, 0, 2);
                layout.SetColumnSpan(aggregateTraffic, 2);
                layout.Controls.Add(bwL, 0, 3);
                layout.Controls.Add(bandwidthCombo, 1, 3);
                layout.Controls.Add(profilesPanel, 0, 4);
                layout.SetColumnSpan(profilesPanel, 2);

                Controls.Add(layout);
                ApplyLiveState();
            }

            public void ApplyLiveState()
            {
                if (IsDisposed) return;
                try
                {
                    RemoteProfile[] snapshot = owner.SnapshotProfiles();
                    int mounted = 0;
                    double aggregateBytesPerSec = 0;
                    long aggregateBytes = 0;
                    for (int i = 0; i < snapshot.Length; i++)
                    {
                        if (owner.IsMounted(snapshot[i]))
                        {
                            mounted++;
                            aggregateBytesPerSec += TrayContext.ParseBytesPerSec(snapshot[i].SpeedText);
                            aggregateBytes += TrayContext.ParseBytes(snapshot[i].SessionText);
                        }
                    }
                    mountSummary.Text = mounted == 0
                        ? "No profiles mounted"
                        : mounted + " of " + snapshot.Length + " profile" + (snapshot.Length == 1 ? "" : "s") + " mounted";
                    aggregateSpeed.Text = TrayContext.FormatBytes(aggregateBytesPerSec) + "/s";
                    aggregateTraffic.Text = "Session traffic: " + TrayContext.FormatBytes(aggregateBytes);

                    int idx = Array.FindIndex(bwChoices, s => String.Equals(s, owner.selectedBandwidth, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0 && bandwidthCombo.SelectedIndex != idx) bandwidthCombo.SelectedIndex = idx;

                    if (perProfileLabels.Count != snapshot.Length)
                    {
                        profilesPanel.SuspendLayout();
                        profilesPanel.Controls.Clear();
                        perProfileLabels.Clear();
                        for (int i = 0; i < snapshot.Length; i++)
                        {
                            Label l = new Label();
                            l.AutoSize = true;
                            l.ForeColor = FgColor;
                            l.Font = new Font("Segoe UI", 9.25f);
                            l.Margin = new Padding(0, 2, 0, 0);
                            profilesPanel.Controls.Add(l);
                            perProfileLabels.Add(l);
                        }
                        profilesPanel.ResumeLayout();
                    }
                    for (int i = 0; i < snapshot.Length && i < perProfileLabels.Count; i++)
                    {
                        RemoteProfile p = snapshot[i];
                        bool m = owner.IsMounted(p);
                        string speed = m ? p.SpeedText : "—";
                        perProfileLabels[i].Text = (m ? "● " : "○ ") + p.Label + "  " + p.DriveLetter + "    " + speed;
                        perProfileLabels[i].ForeColor = m ? FgColor : MutedColor;
                    }
                }
                catch (Exception ex) { owner.LogUiIssue("quick popup live", ex); }
            }
        }
    }
}
