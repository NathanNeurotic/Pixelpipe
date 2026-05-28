using System;
using System.Collections.Generic;
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

        // Held references to dynamic top-level menu items so UpdateMenuLiveState
        // can update their Text / Enabled / Checked without rebuilding the menu.
        private ToolStripMenuItem globalStatusItem;
        private ToolStripMenuItem adminWarningItem;
        private ToolStripMenuItem rcloneStatusItem;
        private ToolStripMenuItem winfspStatusItem;
        private ToolStripMenuItem quotaItem;
        private ToolStripMenuItem mountAllItem;
        private ToolStripMenuItem unmountAllItem;
        private ToolStripMenuItem startupItem;
        private ToolStripMenuItem setupStatusItem;
        private ToolStripMenuItem bandwidthHeaderItem;
        private ToolStripMenuItem updateAvailableItem;
        private readonly List<ProfileMenuRefs> profileMenuRefs = new List<ProfileMenuRefs>();

        private sealed class ProfileMenuRefs
        {
            public RemoteProfile Profile;
            public ToolStripMenuItem ProfileItem;
            public ToolStripMenuItem RemoteLabel;
            public ToolStripMenuItem DriveLabel;
            public ToolStripMenuItem StatusLabel;
            public ToolStripMenuItem StorageLabel;
            public ToolStripMenuItem TransferQuotaLabel;
            public ToolStripMenuItem ObjectsLabel;
            public ToolStripMenuItem TrafficLabel;
            public ToolStripMenuItem SpeedLabel;
            public ToolStripMenuItem WatchLabel;
            public ToolStripMenuItem LastErrorLabel;
            public ToolStripMenuItem MountLow;
            public ToolStripMenuItem MountFull;
            public ToolStripMenuItem Unmount;
            public ToolStripMenuItem OpenDriveItem;
            public ToolStripMenuItem EditItem;
            public ToolStripMenuItem AutoMountItem;
            public ToolStripMenuItem RemoveItem;
        }

        private void OnMenuOpening()
        {
            // If anything in RebuildMenu throws (a corrupt profile, a missing
            // dependency, a renamed icon resource), we still need *some* menu
            // so the user can at least Exit or Open the main window. Without
            // this fallback a single exception leaves the tray icon with an
            // empty context menu, which looks like Pixelpipe is dead. See
            // BuildEmergencyMenu below.
            try { RebuildMenu(); }
            catch (Exception ex)
            {
                LogUiIssue("rebuild menu", ex);
                try { BuildEmergencyMenu(ex); } catch (Exception ex2) { LogUiIssue("emergency menu", ex2); }
            }
            try { RefreshDependencyStatusAsync(false); } catch (Exception ex) { LogUiIssue("dep refresh from menu", ex); }
            try { QueueMenuOpenRefresh(); } catch (Exception ex) { LogUiIssue("queue refresh from menu", ex); }
            try { CheckForUpdatesIfDue(); } catch (Exception ex) { LogUiIssue("update check from menu", ex); }
        }

        // Minimum-viable menu: just enough so the user can read the error and
        // exit. Called only when RebuildMenu throws so the tray icon is never
        // left with a literally-empty popup, which looks identical to "the
        // app crashed" even when only the menu-build path is broken.
        private void BuildEmergencyMenu(Exception ex)
        {
            try
            {
                if (menu == null) return;
                menu.Items.Clear();
                ToolStripMenuItem header = new ToolStripMenuItem("Pixelpipe — menu rebuild failed");
                header.Enabled = false;
                menu.Items.Add(header);
                ToolStripMenuItem detail = new ToolStripMenuItem("Last error: " + (ex == null ? "unknown" : TrimForMenu(ex.Message ?? ex.GetType().Name, 90)));
                detail.Enabled = false;
                menu.Items.Add(detail);
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuAction("Open log folder", delegate { OpenLogFolder(); }));
                menu.Items.Add(MenuAction("Open Pixelpipe window...", delegate { try { ShowMainWindow(); } catch (Exception inner) { LogUiIssue("emergency open window", inner); } }));
                menu.Items.Add(MenuAction("Settings file", delegate { OpenSettingsFile(); }));
                menu.Items.Add(MenuAction("Try rebuilding menu again", delegate { try { RebuildMenu(); } catch (Exception inner) { LogUiIssue("emergency retry rebuild", inner); BuildEmergencyMenu(inner); } }));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuAction("Exit", delegate { ExitApp(); }));
            }
            catch { /* nothing else we can do without crashing the tray */ }
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
            // visibly flash. Defer the rebuild and apply any live updates in place
            // so the user still sees fresh values while the menu is open.
            if (menu != null && menu.Visible)
            {
                rebuildPendingWhileOpen = true;
                UpdateMenuLiveState();
                return;
            }
            menu.Items.Clear();
            profileMenuRefs.Clear();

            AddDisabled("Pixelpipe");
            globalStatusItem = AddDisabledRef("Status: " + BuildGlobalStatus());
            adminWarningItem = AddDisabledRef("Warning: running as Administrator; mounted drives may be hidden from normal Explorer");
            adminWarningItem.Visible = IsAdministrator();
            rcloneStatusItem = AddDisabledRef("rclone: " + (RcloneAvailable() ? "found" : "missing"));
            winfspStatusItem = AddDisabledRef("WinFsp: " + (WinFspInstalled() ? "found" : "missing"));
            quotaItem = AddDisabledRef(transferQuotaText);
            // Sits right under the status block so a fresh update is the first
            // thing the user sees when they right-click the tray.
            updateAvailableItem = MenuAction("Update available — opens releases page", delegate { OpenAvailableUpdate(); });
            updateAvailableItem.Visible = !String.IsNullOrEmpty(availableUpdateVersion);
            if (updateAvailableItem.Visible) updateAvailableItem.Text = "Pixelpipe " + availableUpdateVersion + " available — download";
            menu.Items.Add(updateAvailableItem);
            menu.Items.Add(new ToolStripSeparator());

            InsertWindowShortcuts();

            for (int i = 0; i < profiles.Count; i++)
            {
                RemoteProfile p = profiles[i];
                ProfileMenuRefs refs = BuildProfileMenu(p);
                profileMenuRefs.Add(refs);
                menu.Items.Add(PrepareDropDownMenu(refs.ProfileItem));
            }

            menu.Items.Add(new ToolStripSeparator());
            if (profiles.Count > 1)
            {
                mountAllItem = MenuAction("Mount all", delegate { MountAllProfiles(); }, CountMounted() < profiles.Count);
                unmountAllItem = MenuAction("Unmount all", delegate { UnmountAllProfiles(); }, CountMounted() > 0);
                menu.Items.Add(mountAllItem);
                menu.Items.Add(unmountAllItem);
                menu.Items.Add(new ToolStripSeparator());
            }
            else
            {
                mountAllItem = null;
                unmountAllItem = null;
            }
            menu.Items.Add(BuildAddRemoteMenu());
            menu.Items.Add(BuildBandwidthMenu());
            menu.Items.Add(BuildSetupMenu());
            menu.Items.Add(MenuAction("Import existing rclone remotes", delegate { ImportExistingRemotes(); }));
            menu.Items.Add(MenuAction("Manage remotes...", delegate { ShowManageRemotesWindow(); }));
            menu.Items.Add(BuildToolsMenu());
            menu.Items.Add(new ToolStripSeparator());
            startupItem = MenuAction("Auto-mount at Windows startup", delegate { ToggleStartup(); }, true, StartupEnabled());
            menu.Items.Add(startupItem);
            menu.Items.Add(MenuAction("Exit", delegate { ExitApp(); }));

            UpdateTrayTooltip();
        }

        // Adds "Open Pixelpipe window..." and "Quick controls..." entries plus a
        // separator. Called inline from RebuildMenu so the window shortcuts sit near
        // the top of the menu where the user can find them without scrolling.
        private void InsertWindowShortcuts()
        {
            menu.Items.Add(MenuAction("Open Pixelpipe window...", delegate { ShowMainWindow(); }));
            menu.Items.Add(MenuAction("Quick controls...", delegate { ShowQuickControl(); }));
            menu.Items.Add(new ToolStripSeparator());
        }

        private ProfileMenuRefs BuildProfileMenu(RemoteProfile p)
        {
            ProfileMenuRefs r = new ProfileMenuRefs();
            r.Profile = p;
            r.ProfileItem = new ToolStripMenuItem(ProfileTitle(p));
            r.RemoteLabel = DisabledItem("Remote: " + p.Remote);
            r.DriveLabel = DisabledItem("Drive: " + GetDriveRoot(p));
            ToolStripMenuItem providerLabel = DisabledItem("Provider: " + DisplayProvider(p.Provider));
            r.StatusLabel = DisabledItem("Status: " + p.StatusText);
            r.StorageLabel = DisabledItem("Storage: " + p.StorageText);
            ProviderCapabilities pcap = ProviderCapabilities.For(p.Provider);
            r.TransferQuotaLabel = DisabledItem(pcap.SupportsTransferQuota
                ? (String.IsNullOrEmpty(p.TransferQuotaText) ? pcap.DefaultTransferQuotaText() : p.TransferQuotaText)
                : pcap.DefaultTransferQuotaText());
            r.TransferQuotaLabel.Visible = pcap.SupportsTransferQuota;
            r.ObjectsLabel = DisabledItem(pcap.SupportsFileCount && p.ObjectCount >= 0
                ? "Objects: " + p.ObjectCount.ToString("N0")
                : "");
            r.ObjectsLabel.Visible = pcap.SupportsFileCount && p.ObjectCount >= 0;
            r.TrafficLabel = DisabledItem("Traffic: " + p.SessionText);
            r.SpeedLabel = DisabledItem("Speed: " + p.SpeedText);
            r.WatchLabel = DisabledItem(BuildWatchLabel(p));
            r.WatchLabel.Visible = p.WatchFolderEnabled;
            r.LastErrorLabel = DisabledItem("Last error: " + TrimForMenu(p.LastError, 90));
            r.LastErrorLabel.Visible = !String.IsNullOrWhiteSpace(p.LastError);

            r.ProfileItem.DropDownItems.Add(r.RemoteLabel);
            r.ProfileItem.DropDownItems.Add(r.DriveLabel);
            r.ProfileItem.DropDownItems.Add(providerLabel);
            r.ProfileItem.DropDownItems.Add(r.StatusLabel);
            r.ProfileItem.DropDownItems.Add(r.StorageLabel);
            r.ProfileItem.DropDownItems.Add(r.TransferQuotaLabel);
            r.ProfileItem.DropDownItems.Add(r.ObjectsLabel);
            r.ProfileItem.DropDownItems.Add(r.TrafficLabel);
            r.ProfileItem.DropDownItems.Add(r.SpeedLabel);
            r.ProfileItem.DropDownItems.Add(r.WatchLabel);
            r.ProfileItem.DropDownItems.Add(r.LastErrorLabel);
            r.ProfileItem.DropDownItems.Add(new ToolStripSeparator());
            r.MountLow = MenuAction("Mount - low overhead", delegate { MountProfile(p, false); }, !IsMounted(p));
            r.MountFull = MenuAction("Mount - full cache", delegate { MountProfile(p, true); }, !IsMounted(p));
            r.Unmount = MenuAction("Unmount", delegate { UnmountProfile(p, false); }, IsMounted(p));
            r.OpenDriveItem = MenuAction("Open " + GetDriveRoot(p), delegate { OpenDrive(p); }, IsMounted(p));
            r.ProfileItem.DropDownItems.Add(MenuAction("Test profile", delegate { TestProfile(p); }));
            r.ProfileItem.DropDownItems.Add(r.MountLow);
            r.ProfileItem.DropDownItems.Add(r.MountFull);
            r.ProfileItem.DropDownItems.Add(r.Unmount);
            r.ProfileItem.DropDownItems.Add(r.OpenDriveItem);
            r.ProfileItem.DropDownItems.Add(new ToolStripSeparator());
            r.EditItem = MenuAction("Edit profile...", delegate { EditProfile(p); }, !IsMounted(p));
            r.AutoMountItem = MenuAction("Auto-mount this profile", delegate { ToggleProfileAutoMount(p); }, true, p.AutoMount);
            r.RemoveItem = MenuAction("Remove profile", delegate { RemoveProfile(p); }, !IsMounted(p) && profiles.Count > 1);
            r.ProfileItem.DropDownItems.Add(r.EditItem);
            r.ProfileItem.DropDownItems.Add(MenuAction("Set as primary", delegate { MakePrimaryProfile(p); }));
            r.ProfileItem.DropDownItems.Add(r.AutoMountItem);
            r.ProfileItem.DropDownItems.Add(r.RemoveItem);
            return r;
        }

        // Live update path: called from the refresh worker on the UI thread without
        // rebuilding menu.Items. Safe to call while the menu is open — only edits
        // properties on existing items, which WinForms repaints in place without
        // tearing the menu down.
        private void UpdateMenuLiveState()
        {
            try
            {
                if (menu == null || menu.Items.Count == 0) { return; }
                if (globalStatusItem != null) globalStatusItem.Text = "Status: " + BuildGlobalStatus();
                if (adminWarningItem != null) adminWarningItem.Visible = IsAdministrator();
                if (rcloneStatusItem != null) rcloneStatusItem.Text = "rclone: " + (RcloneAvailable() ? "found" : "missing");
                if (winfspStatusItem != null) winfspStatusItem.Text = "WinFsp: " + (WinFspInstalled() ? "found" : "missing");
                if (quotaItem != null) quotaItem.Text = transferQuotaText;
                if (updateAvailableItem != null)
                {
                    bool show = !String.IsNullOrEmpty(availableUpdateVersion);
                    updateAvailableItem.Visible = show;
                    if (show) updateAvailableItem.Text = "Pixelpipe " + availableUpdateVersion + " available — download";
                }
                if (setupStatusItem != null) setupStatusItem.Text = setupStatusText;
                if (bandwidthHeaderItem != null) bandwidthHeaderItem.Text = "Bandwidth limit: " + DisplayLimit(selectedBandwidth);
                for (int b = 0; b < bandwidthItems.Count; b++)
                {
                    ToolStripMenuItem item = bandwidthItems[b];
                    string value = item.Tag as string;
                    item.Checked = value != null && String.Equals(selectedBandwidth, value, StringComparison.OrdinalIgnoreCase);
                }
                if (startupItem != null) startupItem.Checked = StartupEnabled();

                // Snapshot once for the whole live-update pass so mount-all/remove
                // enabled gating, per-profile updates, and the "more than one
                // profile" remove gating all use a consistent view of the list.
                RemoteProfile[] snapshot = SnapshotProfiles();
                int mountedCount = 0;
                for (int i = 0; i < snapshot.Length; i++) if (IsMounted(snapshot[i])) mountedCount++;
                if (mountAllItem != null) mountAllItem.Enabled = mountedCount < snapshot.Length;
                if (unmountAllItem != null) unmountAllItem.Enabled = mountedCount > 0;
                bool moreThanOne = snapshot.Length > 1;

                // Update per-profile items.
                for (int i = 0; i < profileMenuRefs.Count; i++)
                {
                    ProfileMenuRefs r = profileMenuRefs[i];
                    RemoteProfile p = r.Profile;
                    bool mounted = IsMounted(p);
                    r.ProfileItem.Text = ProfileTitle(p);
                    r.RemoteLabel.Text = "Remote: " + p.Remote;
                    r.DriveLabel.Text = "Drive: " + GetDriveRoot(p);
                    r.StatusLabel.Text = "Status: " + p.StatusText;
                    r.StorageLabel.Text = "Storage: " + p.StorageText;
                    ProviderCapabilities pcap = ProviderCapabilities.For(p.Provider);
                    if (r.TransferQuotaLabel != null)
                    {
                        r.TransferQuotaLabel.Visible = pcap.SupportsTransferQuota;
                        if (pcap.SupportsTransferQuota)
                        {
                            r.TransferQuotaLabel.Text = String.IsNullOrEmpty(p.TransferQuotaText)
                                ? pcap.DefaultTransferQuotaText()
                                : p.TransferQuotaText;
                        }
                    }
                    if (r.ObjectsLabel != null)
                    {
                        bool show = pcap.SupportsFileCount && p.ObjectCount >= 0;
                        r.ObjectsLabel.Visible = show;
                        if (show) r.ObjectsLabel.Text = "Objects: " + p.ObjectCount.ToString("N0");
                    }
                    r.TrafficLabel.Text = "Traffic: " + p.SessionText;
                    r.SpeedLabel.Text = "Speed: " + p.SpeedText;
                    if (r.WatchLabel != null)
                    {
                        r.WatchLabel.Visible = p.WatchFolderEnabled;
                        if (p.WatchFolderEnabled) r.WatchLabel.Text = BuildWatchLabel(p);
                    }
                    bool hasError = !String.IsNullOrWhiteSpace(p.LastError);
                    r.LastErrorLabel.Visible = hasError;
                    if (hasError) r.LastErrorLabel.Text = "Last error: " + TrimForMenu(p.LastError, 90);
                    r.MountLow.Enabled = !mounted;
                    r.MountFull.Enabled = !mounted;
                    r.Unmount.Enabled = mounted;
                    r.OpenDriveItem.Enabled = mounted;
                    r.OpenDriveItem.Text = "Open " + GetDriveRoot(p);
                    r.EditItem.Enabled = !mounted;
                    r.AutoMountItem.Checked = p.AutoMount;
                    r.RemoveItem.Enabled = !mounted && moreThanOne;
                }

                UpdateTrayTooltip();
            }
            catch (Exception ex) { LogUiIssue("update live menu", ex); }
        }

        private static string BuildWatchLabel(RemoteProfile p)
        {
            if (p == null || !p.WatchFolderEnabled) return "Watch: off";
            string mode = TrayContext.NormalizeWatchMode(p.WatchFolderMode);
            string head = "Watch (" + mode + "): " + p.WatchQueueCount + " queued, " + p.WatchUploadingCount + " uploading";
            if (p.WatchFailedTotal > 0) head += ", " + p.WatchFailedTotal + " failed";
            return head;
        }

        private void UpdateTrayTooltip()
        {
            int mountedCount = CountMounted();
            // NotifyIcon.Text has a 63-char limit; keep it short.
            tray.Text = profiles.Count == 0
                ? "Pixelpipe"
                : (mountedCount == 0
                    ? "Pixelpipe (none mounted)"
                    : "Pixelpipe (" + mountedCount + "/" + profiles.Count + " mounted)");
        }

        private ToolStripMenuItem AddDisabledRef(string text)
        {
            ToolStripMenuItem item = DisabledItem(text);
            menu.Items.Add(item);
            return item;
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
            add.DropDownItems.Add(MenuAction("Google Drive (OAuth)", delegate { ConfigureOAuthRemoteWizard("Google Drive", "drive", "drive", "G:"); }));
            add.DropDownItems.Add(MenuAction("OneDrive (OAuth)", delegate { ConfigureOAuthRemoteWizard("OneDrive", "onedrive", "onedrive", "O:"); }));
            add.DropDownItems.Add(MenuAction("Dropbox (OAuth)", delegate { ConfigureOAuthRemoteWizard("Dropbox", "dropbox", "dropbox", "D:"); }));
            add.DropDownItems.Add(MenuAction("Box (OAuth)", delegate { ConfigureOAuthRemoteWizard("Box", "box", "box", "K:"); }));
            add.DropDownItems.Add(MenuAction("MEGA", delegate { ConfigureMegaRemoteWizard(); }));
            add.DropDownItems.Add(MenuAction("S3 / R2 / B2 / Wasabi", delegate { ConfigureS3RemoteWizard(); }));
            add.DropDownItems.Add(MenuAction("WebDAV / Nextcloud / SharePoint", delegate { ConfigureWebDAVRemoteWizard(); }));
            add.DropDownItems.Add(MenuAction("SFTP", delegate { ConfigureSFTPRemoteWizard(); }));
            add.DropDownItems.Add(MenuAction("FTP / FTPS", delegate { ConfigureFTPRemoteWizard(); }));
            add.DropDownItems.Add(new ToolStripSeparator());
            add.DropDownItems.Add(MenuAction("Custom existing rclone remote...", delegate { AddExistingRemoteProfile(); }));
            add.DropDownItems.Add(MenuAction("Open rclone config terminal", delegate { OpenRcloneConfigTerminal(); }));
            return PrepareDropDownMenu(add);
        }

        private ToolStripMenuItem BuildBandwidthMenu()
        {
            bandwidthItems.Clear();
            ToolStripMenuItem m = new ToolStripMenuItem("Bandwidth limit: " + DisplayLimit(selectedBandwidth));
            bandwidthHeaderItem = m;
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
            setupStatusItem = DisabledItem(setupStatusText);
            setup.DropDownItems.Add(setupStatusItem);
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
            tools.DropDownItems.Add(MenuAction("Export profiles to file...", delegate { ExportProfilesToFile(); }));
            tools.DropDownItems.Add(MenuAction("Import profiles from file...", delegate { ImportProfilesFromFile(); }));
            tools.DropDownItems.Add(new ToolStripSeparator());
            tools.DropDownItems.Add(MenuAction("Find / kill orphan rclone processes", delegate { PromptAndKillOrphans(FindOrphanRcloneProcesses(), false); }));
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

    // Shared palette for the MainWindow / QuickControl / SetupWizard / ProfileCard
    // family of dark dialogs. The tray menu has its own slightly darker palette in
    // TrayMenuTheme below; keep both because the tray strip and the dialog windows
    // sit on screen with different parent surfaces (Windows shell vs application
    // background) and the slight contrast difference reads correctly.
    internal static class WindowTheme
    {
        public static readonly Color BgColor = Color.FromArgb(18, 22, 28);
        public static readonly Color CardColor = Color.FromArgb(28, 33, 42);
        // Used as the background for TextBox / ComboBox / ListBox inputs in
        // dialogs — slightly darker than the window background so the field
        // boundaries are visible without a border.
        public static readonly Color InputBg = Color.FromArgb(14, 18, 24);
        public static readonly Color FgColor = Color.WhiteSmoke;
        public static readonly Color MutedColor = Color.FromArgb(160, 170, 184);
        public static readonly Color ButtonBg = Color.FromArgb(48, 53, 64);
        public static readonly Color ButtonBorder = Color.FromArgb(80, 90, 105);
        public static readonly Color AccentColor = Color.FromArgb(110, 200, 255);
        public static readonly Color WarnColor = Color.FromArgb(240, 180, 60);
        public static readonly Color ErrorColor = Color.FromArgb(255, 110, 110);
        public static readonly Color MountedPill = Color.FromArgb(50, 130, 60);
        public static readonly Color UnmountedPill = Color.FromArgb(70, 76, 88);
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
