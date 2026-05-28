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

        // Compact always-on-top window meant for active transfers. Shows aggregate
        // speed + per-profile speed + a bandwidth selector. Stays on top so users
        // can drag it to a corner of the screen.
        private sealed class QuickControlWindow : Form
        {
            private readonly TrayContext owner;
            private Label aggregateSpeed;
            private Label aggregateTraffic;
            private Label mountSummary;
            private ComboBox bandwidthCombo;
            private Panel profilesPanel;
            private readonly List<Label> perProfileLabels = new List<Label>();
            private readonly string[] bwChoices = new string[] { "off", "512K", "1M", "5M", "10M", "25M", "50M", "100M", "250M" };

            public QuickControlWindow(TrayContext owner)
            {
                this.owner = owner;
                Text = "Pixelpipe quick controls";
                StartPosition = FormStartPosition.Manual;
                Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - 360, Screen.PrimaryScreen.WorkingArea.Bottom - 320);
                Width = 340; Height = 280;
                FormBorderStyle = FormBorderStyle.SizableToolWindow;
                TopMost = true;
                BackColor = Color.FromArgb(18, 22, 28);
                ForeColor = Color.WhiteSmoke;
                MinimumSize = new Size(280, 220);

                mountSummary = new Label();
                mountSummary.Left = 12; mountSummary.Top = 8; mountSummary.Width = 300; mountSummary.Height = 20;
                mountSummary.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                mountSummary.ForeColor = Color.WhiteSmoke;

                aggregateSpeed = new Label();
                aggregateSpeed.Left = 12; aggregateSpeed.Top = 32; aggregateSpeed.Width = 300; aggregateSpeed.Height = 24;
                aggregateSpeed.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
                aggregateSpeed.ForeColor = Color.FromArgb(110, 200, 255);

                aggregateTraffic = new Label();
                aggregateTraffic.Left = 12; aggregateTraffic.Top = 60; aggregateTraffic.Width = 300; aggregateTraffic.Height = 20;
                aggregateTraffic.ForeColor = Color.WhiteSmoke;

                Label bwL = new Label();
                bwL.Left = 12; bwL.Top = 92; bwL.Width = 110; bwL.Height = 22;
                bwL.Text = "Bandwidth:";
                bwL.ForeColor = Color.WhiteSmoke;
                bwL.TextAlign = ContentAlignment.MiddleLeft;

                bandwidthCombo = new ComboBox();
                bandwidthCombo.Left = 120; bandwidthCombo.Top = 90; bandwidthCombo.Width = 196;
                bandwidthCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                bandwidthCombo.BackColor = Color.FromArgb(14, 18, 24);
                bandwidthCombo.ForeColor = Color.WhiteSmoke;
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

                profilesPanel = new Panel();
                profilesPanel.Left = 12; profilesPanel.Top = 122;
                profilesPanel.Width = ClientSize.Width - 24;
                profilesPanel.Height = ClientSize.Height - 132;
                profilesPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                profilesPanel.AutoScroll = true;

                Controls.Add(mountSummary);
                Controls.Add(aggregateSpeed);
                Controls.Add(aggregateTraffic);
                Controls.Add(bwL);
                Controls.Add(bandwidthCombo);
                Controls.Add(profilesPanel);

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

                    // Bandwidth combo sync
                    int idx = Array.FindIndex(bwChoices, s => String.Equals(s, owner.selectedBandwidth, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0 && bandwidthCombo.SelectedIndex != idx) bandwidthCombo.SelectedIndex = idx;

                    // Per-profile lines: rebuild if count drifted.
                    if (perProfileLabels.Count != snapshot.Length)
                    {
                        profilesPanel.SuspendLayout();
                        profilesPanel.Controls.Clear();
                        perProfileLabels.Clear();
                        for (int i = 0; i < snapshot.Length; i++)
                        {
                            Label l = new Label();
                            l.Left = 0; l.Top = i * 20; l.Width = profilesPanel.ClientSize.Width - 4; l.Height = 18;
                            l.ForeColor = Color.WhiteSmoke;
                            l.Font = new Font("Segoe UI", 9f);
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
                        perProfileLabels[i].Text = (m ? "● " : "○ ") + p.Label + "  " + p.DriveLetter + "  " + speed;
                        perProfileLabels[i].ForeColor = m ? Color.WhiteSmoke : Color.FromArgb(140, 145, 155);
                    }
                }
                catch (Exception ex) { owner.LogUiIssue("quick popup live", ex); }
            }

        }
    }
}
