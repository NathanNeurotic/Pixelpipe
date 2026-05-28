using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal static class NativeMethods
    {
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }

    internal sealed partial class TrayContext
    {
        private const int MenuOpenRefreshThrottleSeconds = 30;
        private DateTime lastMenuOpenRefreshUtc = DateTime.MinValue;

        private void OnMenuOpening()
        {
            RebuildMenu();
            RefreshDependencyStatusAsync(false);
            QueueMenuOpenRefresh();
        }

        private void QueueMenuOpenRefresh()
        {
            if (refreshingFlag != 0) return;
            if ((DateTime.UtcNow - lastMenuOpenRefreshUtc).TotalSeconds < MenuOpenRefreshThrottleSeconds) return;
            lastMenuOpenRefreshUtc = DateTime.UtcNow;
            QueueRefresh(false, false);
        }

        private void RebuildMenu()
        {
            // If the menu is currently displayed, clearing menu.Items would make it
            // visibly flash — the seven-second timer would cause a blink every tick.
            // Mark the rebuild as pending so the Closed handler picks it up. The
            // Opening handler always calls RebuildMenu first, so the user sees fresh
            // content on the next open either way.
            if (menu != null && menu.Visible)
            {
                rebuildPendingWhileOpen = true;
                return;
            }
            menu.Items.Clear();
            AddDisabled("Pixelpipe");
            AddDisabled("Status: " + BuildGlobalStatus());
            if (IsAdministrator()) AddDisabled("Warning: running as Administrator; mounted drives may be hidden from normal Explorer");
            AddDisabled("rclone: " + (RcloneAvailable() ? "found" : "missing"));
            AddDisabled("WinFsp: " + (WinFspInstalled() ? "found" : "missing"));
            AddDisabled(transferQuotaText);
            menu.Items.Add(new ToolStripSeparator());

            for (int i = 0; i < profiles.Count; i++)
            {
                RemoteProfile p = profiles[i];
                ToolStripMenuItem profileMenu = new ToolStripMenuItem(ProfileTitle(p));
                profileMenu.DropDownItems.Add(DisabledItem("Remote: " + p.Remote));
                profileMenu.DropDownItems.Add(DisabledItem("Drive: " + GetDriveRoot(p)));
                profileMenu.DropDownItems.Add(DisabledItem("Provider: " + DisplayProvider(p.Provider)));
                profileMenu.DropDownItems.Add(DisabledItem("Status: " + p.StatusText));
                profileMenu.DropDownItems.Add(DisabledItem("Storage: " + p.StorageText));
                profileMenu.DropDownItems.Add(DisabledItem("Traffic: " + p.SessionText));
                profileMenu.DropDownItems.Add(DisabledItem("Speed: " + p.SpeedText));
                if (!String.IsNullOrWhiteSpace(p.LastError)) profileMenu.DropDownItems.Add(DisabledItem("Last error: " + TrimForMenu(p.LastError, 90)));
                profileMenu.DropDownItems.Add(new ToolStripSeparator());
                profileMenu.DropDownItems.Add(MenuAction("Mount - low overhead", delegate { MountProfile(p, false); }, !IsMounted(p)));
                profileMenu.DropDownItems.Add(MenuAction("Mount - full cache", delegate { MountProfile(p, true); }, !IsMounted(p)));
                profileMenu.DropDownItems.Add(MenuAction("Unmount", delegate { UnmountProfile(p, false); }, IsMounted(p)));
                profileMenu.DropDownItems.Add(MenuAction("Open " + GetDriveRoot(p), delegate { OpenDrive(p); }, IsMounted(p)));
                profileMenu.DropDownItems.Add(new ToolStripSeparator());
                profileMenu.DropDownItems.Add(MenuAction("Edit profile...", delegate { EditProfile(p); }, !IsMounted(p)));
                profileMenu.DropDownItems.Add(MenuAction("Set as primary", delegate { MakePrimaryProfile(p); }));
                profileMenu.DropDownItems.Add(MenuAction("Auto-mount this profile", delegate { ToggleProfileAutoMount(p); }, true, p.AutoMount));
                profileMenu.DropDownItems.Add(MenuAction("Remove profile", delegate { RemoveProfile(p); }, !IsMounted(p) && profiles.Count > 1));
                menu.Items.Add(PrepareDropDownMenu(profileMenu));
            }

            menu.Items.Add(new ToolStripSeparator());
            if (profiles.Count > 1)
            {
                menu.Items.Add(MenuAction("Mount all", delegate { MountAllProfiles(); }, CountMounted() < profiles.Count));
                menu.Items.Add(MenuAction("Unmount all", delegate { UnmountAllProfiles(); }, CountMounted() > 0));
                menu.Items.Add(new ToolStripSeparator());
            }
            menu.Items.Add(BuildAddRemoteMenu());
            menu.Items.Add(BuildBandwidthMenu());
            menu.Items.Add(BuildSetupMenu());
            menu.Items.Add(MenuAction("Import existing rclone remotes", delegate { ImportExistingRemotes(); }));
            menu.Items.Add(MenuAction("Manage remotes...", delegate { ShowManageRemotesWindow(); }));
            menu.Items.Add(BuildToolsMenu());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuAction("Auto-mount at Windows startup", delegate { ToggleStartup(); }, true, StartupEnabled()));
            menu.Items.Add(MenuAction("Exit", delegate { ExitApp(); }));

            int mountedCount = CountMounted();
            // NotifyIcon.Text has a 63-char limit; keep it short.
            tray.Text = profiles.Count == 0
                ? "Pixelpipe"
                : (mountedCount == 0
                    ? "Pixelpipe (none mounted)"
                    : "Pixelpipe (" + mountedCount + "/" + profiles.Count + " mounted)");
        }

        private void MountAllProfiles()
        {
            RemoteProfile[] snapshot = SnapshotProfiles();
            int started = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (!IsMounted(snapshot[i])) { MountProfile(snapshot[i], snapshot[i].FullCache); started++; }
            }
            if (started == 0) ShowBalloon("All profiles are already mounted.");
        }

        private void UnmountAllProfiles()
        {
            RemoteProfile[] snapshot = SnapshotProfiles();
            int stopped = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (IsMounted(snapshot[i])) { UnmountProfile(snapshot[i], true); stopped++; }
            }
            ShowBalloon(stopped == 0 ? "Nothing was mounted." : "Unmounting " + stopped + " profile(s).");
        }

        private ToolStripMenuItem BuildAddRemoteMenu()
        {
            ToolStripMenuItem add = new ToolStripMenuItem("Add cloud remote");
            add.DropDownItems.Add(MenuAction("Pixeldrain", delegate { AddPixeldrainProfile(); }));
            add.DropDownItems.Add(MenuAction("Google Drive", delegate { AddGuidedRcloneRemote("Google Drive", "drive", "G:"); }));
            add.DropDownItems.Add(MenuAction("MEGA", delegate { AddGuidedRcloneRemote("MEGA", "mega", "M:"); }));
            add.DropDownItems.Add(MenuAction("OneDrive", delegate { AddGuidedRcloneRemote("OneDrive", "onedrive", "O:"); }));
            add.DropDownItems.Add(MenuAction("Dropbox", delegate { AddGuidedRcloneRemote("Dropbox", "dropbox", "D:"); }));
            add.DropDownItems.Add(MenuAction("Box", delegate { AddGuidedRcloneRemote("Box", "box", "K:"); }));
            add.DropDownItems.Add(MenuAction("S3 / R2 / B2 / Wasabi", delegate { AddGuidedRcloneRemote("S3-compatible", "s3", "R:"); }));
            add.DropDownItems.Add(MenuAction("WebDAV / Nextcloud", delegate { AddGuidedRcloneRemote("WebDAV", "webdav", "W:"); }));
            add.DropDownItems.Add(MenuAction("SFTP", delegate { AddGuidedRcloneRemote("SFTP", "sftp", "S:"); }));
            add.DropDownItems.Add(new ToolStripSeparator());
            add.DropDownItems.Add(MenuAction("Custom existing rclone remote...", delegate { AddExistingRemoteProfile(); }));
            add.DropDownItems.Add(MenuAction("Open rclone config terminal", delegate { OpenRcloneConfigTerminal(); }));
            return PrepareDropDownMenu(add);
        }

        private ToolStripMenuItem BuildBandwidthMenu()
        {
            bandwidthItems.Clear();
            ToolStripMenuItem m = new ToolStripMenuItem("Bandwidth limit: " + DisplayLimit(selectedBandwidth));
            AddBandwidthChoice(m, "off", "Unlimited");
            AddBandwidthChoice(m, "512K", "512 KB/s");
            AddBandwidthChoice(m, "1M", "1 MB/s");
            AddBandwidthChoice(m, "5M", "5 MB/s");
            AddBandwidthChoice(m, "10M", "10 MB/s");
            AddBandwidthChoice(m, "25M", "25 MB/s");
            AddBandwidthChoice(m, "50M", "50 MB/s");
            AddBandwidthChoice(m, "100M", "100 MB/s");
            AddBandwidthChoice(m, "250M", "250 MB/s");
            m.DropDownItems.Add(new ToolStripSeparator());
            m.DropDownItems.Add(MenuAction("Custom...", delegate { SetCustomBandwidth(); }));
            return PrepareDropDownMenu(m);
        }

        private ToolStripMenuItem BuildSetupMenu()
        {
            ToolStripMenuItem setup = new ToolStripMenuItem("Setup / dependencies");
            setup.DropDownItems.Add(DisabledItem(setupStatusText));
            setup.DropDownItems.Add(new ToolStripSeparator());
            setup.DropDownItems.Add(MenuAction("Run first-time setup wizard", delegate { RunFirstLaunchSetup(true); }));
            setup.DropDownItems.Add(MenuAction("Download portable rclone now", delegate { DownloadRclonePortableWithUi(); }));
            setup.DropDownItems.Add(MenuAction("Install/update rclone with winget", delegate { InstallRcloneWithWinget(); }));
            setup.DropDownItems.Add(MenuAction("Install WinFsp with winget", delegate { InstallWinFspWithWinget(); }));
            setup.DropDownItems.Add(MenuAction("Configure Pixeldrain remote", delegate { ConfigurePixeldrainRemoteFromPrompt(GetPrimaryProfile()); }));
            setup.DropDownItems.Add(MenuAction("Open rclone config in terminal", delegate { OpenRcloneConfigTerminal(); }));
            setup.DropDownItems.Add(MenuAction("Open winget/App Installer help", delegate { OpenWingetInstallHelp(); }));
            return PrepareDropDownMenu(setup);
        }

        private ToolStripMenuItem BuildToolsMenu()
        {
            ToolStripMenuItem tools = new ToolStripMenuItem("Tools / diagnostics");
            tools.DropDownItems.Add(MenuAction("Diagnostics / repair...", delegate { ShowDiagnosticsWindow(); }));
            tools.DropDownItems.Add(MenuAction("Settings file", delegate { OpenSettingsFile(); }));
            tools.DropDownItems.Add(MenuAction("Open log folder", delegate { OpenLogFolder(); }));
            tools.DropDownItems.Add(MenuAction("Copy diagnostics", delegate { CopyDiagnostics(); }));
            tools.DropDownItems.Add(new ToolStripSeparator());
            tools.DropDownItems.Add(MenuAction("Refresh usage now", delegate { QueueRefresh(true, true); }));
            tools.DropDownItems.Add(MenuAction("Check for updates", delegate { CheckForUpdates(); }));
            return PrepareDropDownMenu(tools);
        }

        private ToolStripMenuItem PrepareDropDownMenu(ToolStripMenuItem item)
        {
            ApplyTrayMenuTheme(item.DropDown);
            item.DropDownOpening += delegate { RepositionDropDownNearOwner(item, "opening"); };
            item.DropDownOpened += delegate { RepositionDropDownNearOwner(item, "opened"); };
            return item;
        }

        private void RepositionDropDownNearOwner(ToolStripMenuItem item, string phase)
        {
            try
            {
                ToolStrip owner = item.Owner;
                ToolStripDropDown dropDown = item.DropDown;
                if (owner == null || dropDown == null)
                {
                    LogUiIssue("tray submenu positioning", new InvalidOperationException(phase + " skipped: owner=" + (owner == null ? "null" : owner.GetType().Name) + " dropDown=" + (dropDown == null ? "null" : dropDown.GetType().Name)));
                    return;
                }

                if (dropDown.IsDisposed || (dropDown.Items.Count == 0)) return;

                dropDown.PerformLayout();
                Size size = dropDown.Size;
                if (size.Width <= 0 || size.Height <= 0) size = dropDown.GetPreferredSize(Size.Empty);
                if (size.Width <= 0 || size.Height <= 0)
                {
                    LogUiIssue("tray submenu positioning", new InvalidOperationException(phase + " skipped for '" + item.Text + "': dropdown has no size after layout"));
                    return;
                }

                Point itemScreenLocation;
                try { itemScreenLocation = owner.PointToScreen(item.Bounds.Location); }
                catch (Exception ex)
                {
                    LogUiIssue("tray submenu positioning", new InvalidOperationException(phase + " PointToScreen failed for '" + item.Text + "': " + ex.Message));
                    itemScreenLocation = Cursor.Position;
                }

                Size itemSize = item.Bounds.Size;
                if (itemSize.Width <= 0) itemSize = new Size(1, itemSize.Height > 0 ? itemSize.Height : 20);
                Rectangle itemBounds = new Rectangle(itemScreenLocation, itemSize);
                Rectangle workingArea = Screen.FromRectangle(itemBounds).WorkingArea;
                Point target = TrayMenuPlacement.CalculateDropDownLocation(itemBounds, size, workingArea);

                LogUiDebug("submenu position " + phase + " '" + item.Text + "': ownerType=" + owner.GetType().Name + " ownerVisible=" + owner.Visible + " item.Bounds=" + item.Bounds + " screenLoc=" + itemScreenLocation + " dropSize=" + size + " workArea=" + workingArea + " target=" + target + " current=" + dropDown.Location);

                dropDown.Location = target;

                if (dropDown.IsHandleCreated)
                {
                    NativeMethods.SetWindowPos(
                        dropDown.Handle,
                        IntPtr.Zero,
                        target.X,
                        target.Y,
                        0,
                        0,
                        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_ASYNCWINDOWPOS);
                }
            }
            catch (Exception ex) { LogUiIssue("tray submenu positioning", ex); }
        }

        private void LogUiDebug(string message)
        {
            if (!verboseLogging) return;
            WriteLogLine("debug", "", message);
        }

        private void AddDisabled(string text)
        {
            menu.Items.Add(DisabledItem(text));
        }

        private ToolStripMenuItem DisabledItem(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Enabled = false;
            return item;
        }

        private ToolStripMenuItem MenuAction(string text, MethodInvoker action)
        {
            return MenuAction(text, action, true, false);
        }

        private ToolStripMenuItem MenuAction(string text, MethodInvoker action, bool enabled)
        {
            return MenuAction(text, action, enabled, false);
        }

        private ToolStripMenuItem MenuAction(string text, MethodInvoker action, bool enabled, bool isChecked)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Enabled = enabled;
            item.Checked = isChecked;
            item.Click += delegate { action(); };
            return item;
        }

        private void AddBandwidthChoice(ToolStripMenuItem parent, string value, string label)
        {
            ToolStripMenuItem item = MenuAction(label, delegate { SetBandwidth(value); });
            item.Tag = value;
            item.Checked = String.Equals(selectedBandwidth, value, StringComparison.OrdinalIgnoreCase);
            parent.DropDownItems.Add(item);
            bandwidthItems.Add(item);
        }

        private void ApplyTrayMenuTheme(ToolStripDropDown strip)
        {
            if (strip == null) return;
            try
            {
                TrayMenuTheme.Apply(strip);
            }
            catch (Exception ex) { LogUiIssue("tray menu theme", ex); }
        }
    }

    internal static class TrayMenuTheme
    {
        public static readonly Color BackColor = Color.FromArgb(14, 18, 24);
        public static readonly Color ForeColor = Color.FromArgb(230, 237, 243);
        public static readonly Padding Padding = new Padding(8, 8, 8, 8);

        public static void Apply(ToolStripDropDown strip)
        {
            if (strip == null) return;
            strip.Renderer = new PixelpipeMenuRenderer();
            strip.BackColor = BackColor;
            strip.ForeColor = ForeColor;
            strip.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            ToolStripDropDownMenu menuStrip = strip as ToolStripDropDownMenu;
            if (menuStrip != null) menuStrip.ShowImageMargin = false;
            strip.Padding = Padding;
        }
    }

    internal sealed class PixelpipeMenuRenderer : ToolStripProfessionalRenderer
    {
        public PixelpipeMenuRenderer() : base(new PixelpipeColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled) e.TextColor = Color.FromArgb(128, 139, 150);
            else e.TextColor = Color.FromArgb(230, 237, 243);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(8, e.Item.Height / 2, e.Item.Width - 16, 1);
            using (Pen p = new Pen(Color.FromArgb(48, 54, 61))) e.Graphics.DrawLine(p, rect.Left, rect.Top, rect.Right, rect.Top);
        }
    }

    internal sealed class PixelpipeColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(14, 18, 24); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(14, 18, 24); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(14, 18, 24); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(14, 18, 24); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(31, 111, 235); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(88, 166, 255); } }
        public override Color MenuBorder { get { return Color.FromArgb(48, 54, 61); } }
        public override Color SeparatorDark { get { return Color.FromArgb(48, 54, 61); } }
        public override Color SeparatorLight { get { return Color.FromArgb(48, 54, 61); } }
    }

    internal static class TrayMenuPlacement
    {
        public static Point CalculateDropDownLocation(Rectangle itemBounds, Size dropDownSize, Rectangle workingArea)
        {
            int x = itemBounds.Right - 2;
            if (x + dropDownSize.Width > workingArea.Right) x = itemBounds.Left - dropDownSize.Width + 2;
            if (x < workingArea.Left) x = workingArea.Left;
            if (x + dropDownSize.Width > workingArea.Right) x = Math.Max(workingArea.Left, workingArea.Right - dropDownSize.Width);

            int y = itemBounds.Top;
            if (y + dropDownSize.Height > workingArea.Bottom) y = Math.Max(workingArea.Top, workingArea.Bottom - dropDownSize.Height);
            if (y < workingArea.Top) y = workingArea.Top;

            return new Point(x, y);
        }
    }

    internal static class TrayMenuPlacementSmokeTest
    {
        public static int Run()
        {
            string themeFailure = ThemeSmokeTest();
            if (themeFailure != null)
            {
                Console.Error.WriteLine("smoketest-menu: theme check failed: " + themeFailure);
                return 10;
            }

            Rectangle screen = new Rectangle(0, 0, 1000, 800);
            if (!Expect(new Rectangle(100, 100, 200, 24), new Size(160, 120), screen, new Point(298, 100), 1)) return 1;
            if (!Expect(new Rectangle(900, 100, 80, 24), new Size(160, 120), screen, new Point(742, 100), 2)) return 2;
            if (!Expect(new Rectangle(10, 740, 80, 24), new Size(160, 120), screen, new Point(88, 680), 3)) return 3;
            if (!Expect(new Rectangle(10, -20, 80, 24), new Size(120, 60), screen, new Point(88, 0), 4)) return 4;
            if (!Expect(new Rectangle(10, 20, 80, 24), new Size(1200, 60), screen, new Point(0, 20), 5)) return 5;
            return 0;
        }

        private static string ThemeSmokeTest()
        {
            using (ContextMenuStrip menu = new ContextMenuStrip())
            {
                ToolStripMenuItem parent = new ToolStripMenuItem("Parent");
                parent.DropDownItems.Add(new ToolStripMenuItem("Child"));
                menu.Items.Add(parent);

                TrayMenuTheme.Apply(menu);
                TrayMenuTheme.Apply(parent.DropDown);

                string menuFailure = WhyNotThemed(menu, "root");
                if (menuFailure != null) return menuFailure;
                return WhyNotThemed(parent.DropDown, "submenu");
            }
        }

        private static string WhyNotThemed(ToolStripDropDown strip, string label)
        {
            if (strip.BackColor != TrayMenuTheme.BackColor)
                return label + " BackColor expected " + TrayMenuTheme.BackColor + " got " + strip.BackColor;
            if (strip.ForeColor != TrayMenuTheme.ForeColor)
                return label + " ForeColor expected " + TrayMenuTheme.ForeColor + " got " + strip.ForeColor;
            if (!(strip.Renderer is PixelpipeMenuRenderer))
                return label + " Renderer expected PixelpipeMenuRenderer got " + (strip.Renderer == null ? "null" : strip.Renderer.GetType().Name);
            ToolStripDropDownMenu menu = strip as ToolStripDropDownMenu;
            if (menu != null && menu.ShowImageMargin)
                return label + " ShowImageMargin expected false got true";
            return null;
        }

        private static bool Expect(Rectangle itemBounds, Size dropDownSize, Rectangle workingArea, Point expected, int caseIndex)
        {
            Point actual = TrayMenuPlacement.CalculateDropDownLocation(itemBounds, dropDownSize, workingArea);
            if (actual != expected || !workingArea.Contains(actual))
            {
                Console.Error.WriteLine("smoketest-menu: placement case " + caseIndex + " expected " + expected + " got " + actual);
                return false;
            }
            return true;
        }
    }
}
