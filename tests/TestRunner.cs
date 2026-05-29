using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pixelpipe.Tests
{
    internal static class TestRunner
    {
        private static int failures = 0;
        private static int total = 0;
        private static string currentTest = "";

        public static int Main(string[] args)
        {
            Run("FormatBytes", TestFormatBytes);
            Run("DisplayLimit", TestDisplayLimit);
            Run("NormalizeDriveLetter", TestNormalizeDriveLetter);
            Run("NormalizeRemoteName", TestNormalizeRemoteName);
            Run("RemoteNameBare", TestRemoteNameBare);
            Run("NormalizeMountMode", TestNormalizeMountMode);
            Run("NormalizeProvider", TestNormalizeProvider);
            Run("DisplayProvider", TestDisplayProvider);
            Run("ToStringValue", TestToStringValue);
            Run("ToBool", TestToBool);
            Run("SafeFileName", TestSafeFileName);
            Run("TrimForMenu", TestTrimForMenu);
            Run("QuoteArg", TestQuoteArg);
            Run("TrayMenuPlacement", TestTrayMenuPlacement);
            Run("HasArg", TestHasArg);
            Run("ProfilePortFor", TestProfilePortFor);
            Run("IsValidBandwidth", TestIsValidBandwidth);
            Run("NormalizeBandwidthLimit", TestNormalizeBandwidthLimit);
            Run("WriteAllTextAtomic", TestWriteAllTextAtomic);
            Run("PreflightFormatting", TestPreflightFormatting);
            Run("PreflightShortSummary", TestPreflightShortSummary);
            Run("FirstNonEmptyLine", TestFirstNonEmptyLine);
            Run("IndentLines", TestIndentLines);
            Run("IsNewer", TestIsNewer);
            Run("ScheduleAllowsDay", TestScheduleAllowsDay);
            Run("TryNormalizeScheduleTime", TestTryNormalizeScheduleTime);
            Run("ScheduleTimeMatches", TestScheduleTimeMatches);
            Run("ScrubSecrets", TestScrubSecrets);
            Run("BoxProvider", TestBoxProvider);
            Run("ParseBytesPerSec", TestParseBytesPerSec);
            Run("ParseBytes", TestParseBytes);
            Run("ParseStoragePercent", TestParseStoragePercent);
            Run("ComputeStoragePercent", TestComputeStoragePercent);
            Run("FilterLogText", TestFilterLogText);
            Run("ProviderCapabilitiesDefaults", TestProviderCapabilitiesDefaults);
            Run("BuildProfilesExportJson", TestBuildProfilesExportJson);
            Run("TryParseProfilesExportJson", TestTryParseProfilesExportJson);
            Run("PlanProfileImport", TestPlanProfileImport);
            Run("BuildRcloneConfigCreateArgs", TestBuildRcloneConfigCreateArgs);
            Run("NormalizeWatchMode", TestNormalizeWatchMode);
            Run("BuildWatchUploadArgs", TestBuildWatchUploadArgs);
            Run("ComputeWatchNextRetryUtc", TestComputeWatchNextRetryUtc);
            Run("CommandLineMentionsDrive", TestCommandLineMentionsDrive);
            Run("ParseBandwidthSchedule", TestParseBandwidthSchedule);
            Run("IsSecretField", TestIsSecretField);
            Run("MergeRcloneConfigSection", TestMergeRcloneConfigSection);
            Run("ParseSha256ForFile", TestParseSha256ForFile);
            Run("ProcessResultSucceeded", TestProcessResultSucceeded);
            Run("ClassifyActivity", TestClassifyActivity);
            Run("FormatActivityEvents", TestFormatActivityEvents);
            Run("ParseActivityLog", TestParseActivityLog);

            Console.WriteLine();
            Console.WriteLine(total - failures + " / " + total + " passed");
            return failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action body)
        {
            currentTest = name;
            try
            {
                body();
                Console.WriteLine("ok    " + name);
            }
            catch (AssertionException ex)
            {
                Console.WriteLine("FAIL  " + name + ": " + ex.Message);
                failures++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR " + name + ": " + ex.GetType().Name + ": " + ex.Message);
                failures++;
            }
            finally
            {
                total++;
            }
        }

        private static void TestFormatBytes()
        {
            AssertEqual("unknown", TrayContext.FormatBytes(-1));
            AssertEqual("0 B", TrayContext.FormatBytes(0));
            AssertEqual("512 B", TrayContext.FormatBytes(512));
            AssertEqual("1 KB", TrayContext.FormatBytes(1024));
            AssertEqual("1.5 KB", TrayContext.FormatBytes(1536));
            AssertEqual("1 MB", TrayContext.FormatBytes(1024L * 1024));
            AssertEqual("1 GB", TrayContext.FormatBytes(1024L * 1024 * 1024));
            AssertEqual("1 TB", TrayContext.FormatBytes(1024L * 1024 * 1024 * 1024));
        }

        private static void TestDisplayLimit()
        {
            AssertEqual("Unlimited", TrayContext.DisplayLimit("off"));
            AssertEqual("Unlimited", TrayContext.DisplayLimit("OFF"));
            AssertEqual("Unlimited", TrayContext.DisplayLimit(""));
            AssertEqual("Unlimited", TrayContext.DisplayLimit(null));
            AssertEqual("1M/s", TrayContext.DisplayLimit("1M"));
            AssertEqual("512K/s", TrayContext.DisplayLimit("512K"));
        }

        private static void TestNormalizeDriveLetter()
        {
            AssertEqual("P:", TrayContext.NormalizeDriveLetter("p"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetter("P"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetter("P:"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetter("p:"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetter("P:\\"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetter("p:\\foo\\bar"));
            AssertEqual("Z:", TrayContext.NormalizeDriveLetter("z"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetter(""));
            AssertEqual("P:", TrayContext.NormalizeDriveLetter(null));
            AssertEqual("P:", TrayContext.NormalizeDriveLetter("garbage"));
        }

        private static void TestNormalizeRemoteName()
        {
            AssertEqual("foo:", TrayContext.NormalizeRemoteName("foo"));
            AssertEqual("foo:", TrayContext.NormalizeRemoteName("foo:"));
            AssertEqual("Pixeldrain:", TrayContext.NormalizeRemoteName("Pixeldrain"));
            AssertEqual("Pixeldrain:", TrayContext.NormalizeRemoteName(""));
            AssertEqual("Pixeldrain:", TrayContext.NormalizeRemoteName(null));
            AssertEqual("Pixeldrain:", TrayContext.NormalizeRemoteName("   "));
        }

        private static void TestRemoteNameBare()
        {
            AssertEqual("foo", TrayContext.RemoteNameBare("foo:"));
            AssertEqual("foo", TrayContext.RemoteNameBare("foo"));
            AssertEqual("Pixeldrain", TrayContext.RemoteNameBare("Pixeldrain:"));
            AssertEqual("Pixeldrain", TrayContext.RemoteNameBare(null));
        }

        private static void TestNormalizeMountMode()
        {
            AssertEqual("fixed", TrayContext.NormalizeMountMode("fixed"));
            AssertEqual("fixed", TrayContext.NormalizeMountMode("FIXED"));
            AssertEqual("network", TrayContext.NormalizeMountMode("network"));
            AssertEqual("network", TrayContext.NormalizeMountMode("NETWORK"));
            AssertEqual("network", TrayContext.NormalizeMountMode(""));
            AssertEqual("network", TrayContext.NormalizeMountMode(null));
            AssertEqual("network", TrayContext.NormalizeMountMode("garbage"));
        }

        private static void TestNormalizeProvider()
        {
            AssertEqual("pixeldrain", TrayContext.NormalizeProvider("pixeldrain", ""));
            AssertEqual("pixeldrain", TrayContext.NormalizeProvider("Pixeldrain", ""));
            AssertEqual("pixeldrain", TrayContext.NormalizeProvider("", "Pixeldrain:"));
            AssertEqual("drive", TrayContext.NormalizeProvider("drive", ""));
            AssertEqual("drive", TrayContext.NormalizeProvider("google", ""));
            AssertEqual("mega", TrayContext.NormalizeProvider("mega", ""));
            AssertEqual("onedrive", TrayContext.NormalizeProvider("onedrive", ""));
            AssertEqual("dropbox", TrayContext.NormalizeProvider("dropbox", ""));
            AssertEqual("box", TrayContext.NormalizeProvider("box", ""));
            AssertEqual("s3", TrayContext.NormalizeProvider("s3", ""));
            AssertEqual("s3", TrayContext.NormalizeProvider("b2", ""));
            AssertEqual("s3", TrayContext.NormalizeProvider("r2", ""));
            AssertEqual("s3", TrayContext.NormalizeProvider("wasabi", ""));
            AssertEqual("webdav", TrayContext.NormalizeProvider("webdav", ""));
            AssertEqual("webdav", TrayContext.NormalizeProvider("nextcloud", ""));
            AssertEqual("sftp", TrayContext.NormalizeProvider("sftp", ""));
            AssertEqual("ftp", TrayContext.NormalizeProvider("ftp", ""));
            AssertEqual("custom", TrayContext.NormalizeProvider("", ""));
            AssertEqual("xyz", TrayContext.NormalizeProvider("xyz", ""));
        }

        private static void TestDisplayProvider()
        {
            AssertEqual("Pixeldrain", TrayContext.DisplayProvider("pixeldrain"));
            AssertEqual("Google Drive", TrayContext.DisplayProvider("drive"));
            AssertEqual("MEGA", TrayContext.DisplayProvider("mega"));
            AssertEqual("OneDrive", TrayContext.DisplayProvider("onedrive"));
            AssertEqual("Dropbox", TrayContext.DisplayProvider("dropbox"));
            AssertEqual("Box", TrayContext.DisplayProvider("box"));
            AssertEqual("S3-compatible", TrayContext.DisplayProvider("s3"));
            AssertEqual("WebDAV", TrayContext.DisplayProvider("webdav"));
            AssertEqual("SFTP", TrayContext.DisplayProvider("sftp"));
            AssertEqual("FTP", TrayContext.DisplayProvider("ftp"));
            AssertEqual("Custom", TrayContext.DisplayProvider("xyz"));
            AssertEqual("Custom", TrayContext.DisplayProvider(""));
        }

        private static void TestToStringValue()
        {
            AssertEqual("fallback", TrayContext.ToStringValue(null, "fallback"));
            AssertEqual("fallback", TrayContext.ToStringValue("", "fallback"));
            AssertEqual("fallback", TrayContext.ToStringValue("   ", "fallback"));
            AssertEqual("hello", TrayContext.ToStringValue("hello", "fallback"));
            AssertEqual("42", TrayContext.ToStringValue(42, "fallback"));
        }

        private static void TestToBool()
        {
            AssertTrue(TrayContext.ToBool(true));
            AssertFalse(TrayContext.ToBool(false));
            AssertTrue(TrayContext.ToBool("true"));
            AssertTrue(TrayContext.ToBool("True"));
            AssertTrue(TrayContext.ToBool("TRUE"));
            AssertTrue(TrayContext.ToBool("1"));
            AssertTrue(TrayContext.ToBool("yes"));
            AssertFalse(TrayContext.ToBool("false"));
            AssertFalse(TrayContext.ToBool("0"));
            AssertFalse(TrayContext.ToBool("no"));
            AssertFalse(TrayContext.ToBool(""));
            AssertFalse(TrayContext.ToBool(null));
            AssertFalse(TrayContext.ToBool("garbage"));
        }

        private static void TestSafeFileName()
        {
            AssertEqual("remote", TrayContext.SafeFileName(""));
            AssertEqual("remote", TrayContext.SafeFileName(null));
            AssertEqual("remote", TrayContext.SafeFileName("   "));
            AssertEqual("plain", TrayContext.SafeFileName("plain"));
            AssertContains(TrayContext.SafeFileName("foo:bar"), "_");
            AssertFalse(TrayContext.SafeFileName("foo:bar*baz").Contains(":"));
            AssertFalse(TrayContext.SafeFileName("foo:bar*baz").Contains("*"));
        }

        private static void TestTrimForMenu()
        {
            AssertEqual("short", TrayContext.TrimForMenu("short", 10));
            AssertEqual("12345", TrayContext.TrimForMenu("12345", 5));
            AssertEqual("12345...", TrayContext.TrimForMenu("1234567890", 5));
            AssertEqual("", TrayContext.TrimForMenu("", 5));
            AssertEqual(null, TrayContext.TrimForMenu(null, 5));
        }

        private static void TestQuoteArg()
        {
            AssertEqual("\"\"", TrayContext.QuoteArg(null));
            AssertEqual("\"remote name:\"", TrayContext.QuoteArg("remote name:"));
            AssertEqual("\"a\\\"b\"", TrayContext.QuoteArg("a\"b"));
            AssertEqual("\"C:\\temp\\\\\"", TrayContext.QuoteArg("C:\\temp\\"));
        }

        private static void TestTrayMenuPlacement()
        {
            System.Drawing.Rectangle screen = new System.Drawing.Rectangle(0, 0, 1000, 800);
            AssertEqual(
                new System.Drawing.Point(298, 100),
                TrayMenuPlacement.CalculateDropDownLocation(new System.Drawing.Rectangle(100, 100, 200, 24), new System.Drawing.Size(160, 120), screen));
            AssertEqual(
                new System.Drawing.Point(742, 100),
                TrayMenuPlacement.CalculateDropDownLocation(new System.Drawing.Rectangle(900, 100, 80, 24), new System.Drawing.Size(160, 120), screen));
            AssertEqual(
                new System.Drawing.Point(88, 680),
                TrayMenuPlacement.CalculateDropDownLocation(new System.Drawing.Rectangle(10, 740, 80, 24), new System.Drawing.Size(160, 120), screen));
            AssertEqual(
                new System.Drawing.Point(88, 0),
                TrayMenuPlacement.CalculateDropDownLocation(new System.Drawing.Rectangle(10, -20, 80, 24), new System.Drawing.Size(120, 60), screen));
            AssertEqual(
                new System.Drawing.Point(0, 20),
                TrayMenuPlacement.CalculateDropDownLocation(new System.Drawing.Rectangle(10, 20, 80, 24), new System.Drawing.Size(1200, 60), screen));
        }

        private static void TestHasArg()
        {
            AssertTrue(Program.HasArg(new string[] { "/foo", "/bar" }, "/foo"));
            AssertTrue(Program.HasArg(new string[] { "/FOO" }, "/foo"));
            AssertTrue(Program.HasArg(new string[] { "/automount" }, "/AUTOMOUNT"));
            AssertFalse(Program.HasArg(new string[] { "/bar" }, "/foo"));
            AssertFalse(Program.HasArg(new string[0], "/foo"));
            AssertFalse(Program.HasArg(null, "/foo"));
        }

        private static void TestProfilePortFor()
        {
            int a = TrayContext.ProfilePortFor("abc123");
            int b = TrayContext.ProfilePortFor("abc123");
            AssertEqual(a, b); // deterministic
            AssertTrue(a >= 55729 && a < 62729);

            int c = TrayContext.ProfilePortFor("xyz999");
            AssertTrue(c >= 55729 && c < 62729);

            int empty = TrayContext.ProfilePortFor("");
            AssertEqual(55729, empty); // empty hash maps to base
        }

        private static void TestIsValidBandwidth()
        {
            AssertTrue(TrayContext.IsValidBandwidth("off"));
            AssertTrue(TrayContext.IsValidBandwidth("OFF"));
            AssertTrue(TrayContext.IsValidBandwidth("512K"));
            AssertTrue(TrayContext.IsValidBandwidth("1M"));
            AssertTrue(TrayContext.IsValidBandwidth("1.5G"));
            AssertTrue(TrayContext.IsValidBandwidth("10"));
            AssertFalse(TrayContext.IsValidBandwidth(""));
            AssertFalse(TrayContext.IsValidBandwidth(null));
            AssertFalse(TrayContext.IsValidBandwidth("garbage"));
            AssertFalse(TrayContext.IsValidBandwidth("1MB"));
            AssertFalse(TrayContext.IsValidBandwidth("-5M"));
        }

        private static void TestNormalizeBandwidthLimit()
        {
            AssertEqual("off", TrayContext.NormalizeBandwidthLimit(null));
            AssertEqual("off", TrayContext.NormalizeBandwidthLimit(""));
            AssertEqual("off", TrayContext.NormalizeBandwidthLimit("garbage"));
            AssertEqual("1M", TrayContext.NormalizeBandwidthLimit(" 1M "));
            AssertEqual("OFF", TrayContext.NormalizeBandwidthLimit("OFF"));
        }

        private static void TestWriteAllTextAtomic()
        {
            string dir = Path.Combine(Path.GetTempPath(), "Pixelpipe.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "settings.json");
                File.WriteAllText(path, "old", Encoding.UTF8);
                TrayContext.WriteAllTextAtomic(path, "new", Encoding.UTF8);
                AssertEqual("new", File.ReadAllText(path, Encoding.UTF8));
                AssertEqual("old", File.ReadAllText(path + ".bak", Encoding.UTF8));
                AssertFalse(File.Exists(path + ".tmp"));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void TestPreflightFormatting()
        {
            string ok = TrayContext.FormatPreflightLine("ok", "rclone", "found");
            AssertEqual("[OK] rclone: found", ok);
            AssertFalse(TrayContext.PreflightHasFailures(ok));
            AssertFalse(TrayContext.PreflightHasWarnings(ok));

            string warn = ok + Environment.NewLine + TrayContext.FormatPreflightLine("warn", "storage", "not reported");
            AssertTrue(TrayContext.PreflightHasWarnings(warn));
            AssertFalse(TrayContext.PreflightHasFailures(warn));

            string fail = warn + Environment.NewLine + TrayContext.FormatPreflightLine("fail", "remote", "missing");
            AssertTrue(TrayContext.PreflightHasFailures(fail));
        }

        private static void TestPreflightShortSummary()
        {
            // No failures and no warnings -> empty string.
            string allOk = TrayContext.FormatPreflightLine("OK", "rclone", "found") + Environment.NewLine +
                           TrayContext.FormatPreflightLine("OK", "WinFsp", "installed");
            AssertEqual("", TrayContext.PreflightShortSummary(allOk));

            // Warning only -> returns the first [WARN] line.
            string warnOnly = TrayContext.FormatPreflightLine("OK", "rclone", "found") + Environment.NewLine +
                              TrayContext.FormatPreflightLine("WARN", "storage probe", "not reported") + Environment.NewLine +
                              TrayContext.FormatPreflightLine("WARN", "rclone version", "stale");
            AssertEqual("[WARN] storage probe: not reported", TrayContext.PreflightShortSummary(warnOnly));

            // Failure -> returns the first [FAIL] line, even if warnings precede it.
            string mixed = TrayContext.FormatPreflightLine("WARN", "storage probe", "not reported") + Environment.NewLine +
                           TrayContext.FormatPreflightLine("FAIL", "rclone remote", "missing") + Environment.NewLine +
                           TrayContext.FormatPreflightLine("FAIL", "WinFsp", "not detected");
            AssertEqual("[FAIL] rclone remote: missing", TrayContext.PreflightShortSummary(mixed));

            // Null / empty -> empty string.
            AssertEqual("", TrayContext.PreflightShortSummary(null));
            AssertEqual("", TrayContext.PreflightShortSummary(""));
        }

        private static void TestIndentLines()
        {
            AssertEqual("", TrayContext.IndentLines(null, "  "));
            AssertEqual("", TrayContext.IndentLines("", "  "));
            // Single line gets the indent prefix.
            AssertEqual("    hello", TrayContext.IndentLines("hello", "    "));
            // Multi-line: every non-empty line gets the prefix, empty lines stay empty.
            string input = "[OK] rclone: found\r\n[FAIL] WinFsp: missing\r\n\r\n[OK] drive: P:";
            string expected = "    [OK] rclone: found" + Environment.NewLine +
                              "    [FAIL] WinFsp: missing" + Environment.NewLine +
                              Environment.NewLine +
                              "    [OK] drive: P:";
            AssertEqual(expected, TrayContext.IndentLines(input, "    "));
        }

        private static void TestIsNewer()
        {
            // Plain semver bump.
            AssertTrue(TrayContext.IsNewer("0.7.0", "0.6.1"));
            AssertTrue(TrayContext.IsNewer("v0.7.0", "0.6.1"));
            AssertTrue(TrayContext.IsNewer("V1.0.0", "0.9.9"));

            // Equal versions are not newer (even with v prefix and zero padding).
            AssertFalse(TrayContext.IsNewer("v0.6.1", "0.6.1"));
            AssertFalse(TrayContext.IsNewer("0.7.0", "0.7.0.0"));

            // Older remote.
            AssertFalse(TrayContext.IsNewer("v0.5.0", "0.6.1"));

            // Unparseable / empty inputs return false (no false notifications).
            AssertFalse(TrayContext.IsNewer("", "0.6.1"));
            AssertFalse(TrayContext.IsNewer(null, "0.6.1"));
            AssertFalse(TrayContext.IsNewer("v0.6.1", ""));
            AssertFalse(TrayContext.IsNewer("v0.6.1", null));
            AssertFalse(TrayContext.IsNewer("not-a-version", "0.6.1"));
            AssertFalse(TrayContext.IsNewer("v0.6.1", "not-a-version"));

            // Each component is compared independently.
            AssertTrue(TrayContext.IsNewer("v0.6.10", "0.6.9"));
            AssertTrue(TrayContext.IsNewer("v1.0.0", "0.999.999"));
            AssertFalse(TrayContext.IsNewer("v0.6.9", "0.6.10"));
        }

        private static void TestScheduleAllowsDay()
        {
            // Empty/null day list is interpreted as "all seven days" so older
            // profiles without ScheduleDays keep their pre-v0.8 behaviour.
            AssertTrue(TrayContext.ScheduleAllowsDay(null, DayOfWeek.Monday));
            AssertTrue(TrayContext.ScheduleAllowsDay("", DayOfWeek.Sunday));
            AssertTrue(TrayContext.ScheduleAllowsDay("   ", DayOfWeek.Wednesday));

            // Standard subset.
            AssertTrue(TrayContext.ScheduleAllowsDay("Mon,Tue,Wed,Thu,Fri", DayOfWeek.Wednesday));
            AssertFalse(TrayContext.ScheduleAllowsDay("Mon,Tue,Wed,Thu,Fri", DayOfWeek.Saturday));

            // Case-insensitive.
            AssertTrue(TrayContext.ScheduleAllowsDay("mon,WED", DayOfWeek.Wednesday));

            // Whitespace tolerated.
            AssertTrue(TrayContext.ScheduleAllowsDay(" Mon ,  Fri ", DayOfWeek.Friday));
            AssertFalse(TrayContext.ScheduleAllowsDay("Mon, Fri", DayOfWeek.Tuesday));

            // Garbage tokens are ignored without throwing.
            AssertTrue(TrayContext.ScheduleAllowsDay("Mon,Cthulhu,Wed", DayOfWeek.Wednesday));
            AssertFalse(TrayContext.ScheduleAllowsDay("Cthulhu", DayOfWeek.Monday));
        }

        private static void TestTryNormalizeScheduleTime()
        {
            string n;
            AssertTrue(TrayContext.TryNormalizeScheduleTime("9:00", out n));
            AssertEqual("09:00", n);
            AssertTrue(TrayContext.TryNormalizeScheduleTime("09:00", out n));
            AssertEqual("09:00", n);
            AssertTrue(TrayContext.TryNormalizeScheduleTime("  23:59 ", out n));
            AssertEqual("23:59", n);
            AssertTrue(TrayContext.TryNormalizeScheduleTime("0:0", out n));
            AssertEqual("00:00", n);

            // Out-of-range and malformed inputs return false.
            AssertFalse(TrayContext.TryNormalizeScheduleTime("", out n));
            AssertFalse(TrayContext.TryNormalizeScheduleTime(null, out n));
            AssertFalse(TrayContext.TryNormalizeScheduleTime("24:00", out n));
            AssertFalse(TrayContext.TryNormalizeScheduleTime("12:60", out n));
            AssertFalse(TrayContext.TryNormalizeScheduleTime("-1:00", out n));
            AssertFalse(TrayContext.TryNormalizeScheduleTime("abc", out n));
            AssertFalse(TrayContext.TryNormalizeScheduleTime("12:", out n));
            AssertFalse(TrayContext.TryNormalizeScheduleTime(":30", out n));
        }

        private static void TestScheduleTimeMatches()
        {
            AssertTrue(TrayContext.ScheduleTimeMatches("9:00", "09:00"));
            AssertTrue(TrayContext.ScheduleTimeMatches("09:00", "09:00"));
            AssertTrue(TrayContext.ScheduleTimeMatches("  23:59 ", "23:59"));
            AssertFalse(TrayContext.ScheduleTimeMatches("9:00", "09:01"));
            AssertFalse(TrayContext.ScheduleTimeMatches("garbage", "12:00"));
            AssertFalse(TrayContext.ScheduleTimeMatches("", "12:00"));
            AssertFalse(TrayContext.ScheduleTimeMatches(null, "12:00"));
        }

        private static void TestFirstNonEmptyLine()
        {
            AssertEqual("", TrayContext.FirstNonEmptyLine(null));
            AssertEqual("", TrayContext.FirstNonEmptyLine(""));
            AssertEqual("rclone v1.71.1", TrayContext.FirstNonEmptyLine("\r\n  \n rclone v1.71.1\nmore"));
        }

        private static void TestScrubSecrets()
        {
            string scrubbed = TrayContext.ScrubSecrets("api_key=abcdef1234567890");
            AssertContains(scrubbed, "api_key=***");
            AssertFalse(scrubbed.Contains("abcdef1234567890"));

            string longToken = TrayContext.ScrubSecrets("token here: " + new string('A', 40));
            AssertContains(longToken, "***");

            string benign = TrayContext.ScrubSecrets("rclone version v1.71.1");
            AssertEqual("rclone version v1.71.1", benign);

            AssertEqual(null, TrayContext.ScrubSecrets(null));
            AssertEqual("", TrayContext.ScrubSecrets(""));
        }

        private static void TestBoxProvider()
        {
            // Box has to be matched by exact string, not IndexOf, because "box" appears
            // inside words like "dropbox".
            AssertEqual("box", TrayContext.NormalizeProvider("box", ""));
            AssertEqual("dropbox", TrayContext.NormalizeProvider("dropbox", ""));
            AssertEqual("Box", TrayContext.DisplayProvider("box"));
            AssertEqual("Dropbox", TrayContext.DisplayProvider("dropbox"));
        }

        private static void TestParseBytesPerSec()
        {
            AssertEqual(0d, TrayContext.ParseBytesPerSec(""));
            AssertEqual(0d, TrayContext.ParseBytesPerSec(null));
            AssertEqual(0d, TrayContext.ParseBytesPerSec("unavailable"));
            AssertEqual(0d, TrayContext.ParseBytesPerSec("—")); // em-dash placeholder
            AssertEqual(0d, TrayContext.ParseBytesPerSec("-5 MB/s")); // negative clamped to 0
            AssertEqual(512d, TrayContext.ParseBytesPerSec("512 B/s"));
            AssertEqual(1024d, TrayContext.ParseBytesPerSec("1 KB/s"));
            AssertEqual(1024d * 1024, TrayContext.ParseBytesPerSec("1 MB/s"));
            AssertEqual(12.5 * 1024 * 1024, TrayContext.ParseBytesPerSec("12.5 MB/s"));
        }

        private static void TestParseBytes()
        {
            AssertEqual(0L, TrayContext.ParseBytes(""));
            AssertEqual(0L, TrayContext.ParseBytes(null));
            AssertEqual(0L, TrayContext.ParseBytes("not mounted"));
            AssertEqual(512L, TrayContext.ParseBytes("512 B"));
            AssertEqual(1024L, TrayContext.ParseBytes("1 KB"));
            AssertEqual(1024L * 1024 * 1024, TrayContext.ParseBytes("1 GB"));
            AssertEqual((long)(1.11 * 1024 * 1024 * 1024), TrayContext.ParseBytes("1.11 GB"));
        }

        private static void TestParseStoragePercent()
        {
            AssertEqual(0, TrayContext.ParseStoragePercent(""));
            AssertEqual(0, TrayContext.ParseStoragePercent(null));
            AssertEqual(0, TrayContext.ParseStoragePercent("1.11 GB used"));
            AssertEqual(0, TrayContext.ParseStoragePercent("1.11 GB / 7.28 TB used (0%, 7.27 TB left, 30d)"));
            AssertEqual(50, TrayContext.ParseStoragePercent("3 GB / 6 GB used (50%, 3 GB left, 30d)"));
            AssertEqual(99, TrayContext.ParseStoragePercent("x (99.4%)"));
            AssertEqual(100, TrayContext.ParseStoragePercent("(150%)")); // clamped
        }

        private static void TestComputeStoragePercent()
        {
            // Missing data returns -1 so the caller knows to fall back.
            AssertEqual(-1, TrayContext.ComputeStoragePercent(-1, 100));
            AssertEqual(-1, TrayContext.ComputeStoragePercent(5, 0));
            AssertEqual(-1, TrayContext.ComputeStoragePercent(5, -1));

            AssertEqual(0, TrayContext.ComputeStoragePercent(0, 100));
            AssertEqual(50, TrayContext.ComputeStoragePercent(50, 100));
            AssertEqual(100, TrayContext.ComputeStoragePercent(100, 100));
            // Over-budget clamped.
            AssertEqual(100, TrayContext.ComputeStoragePercent(150, 100));
            // Rounded.
            AssertEqual(33, TrayContext.ComputeStoragePercent(1, 3));
        }

        private static void TestFilterLogText()
        {
            // Empty/null inputs pass through cleanly.
            AssertEqual("", TrayContext.FilterLogText(null, "anything"));
            AssertEqual("", TrayContext.FilterLogText("", "anything"));
            // Empty filter returns input untouched.
            AssertEqual("hello\nworld", TrayContext.FilterLogText("hello\nworld", ""));
            AssertEqual("hello\nworld", TrayContext.FilterLogText("hello\nworld", null));
            // Case-insensitive substring match, line-by-line.
            string log = "2026-05-28 mount profile=Pixeldrain\r\n2026-05-28 unmount profile=Drive\r\n2026-05-28 mount profile=Pixeldrain done";
            string filtered = TrayContext.FilterLogText(log, "pixeldrain");
            AssertContains(filtered, "Pixeldrain");
            AssertFalse(filtered.Contains("Drive\r") || filtered.Contains("Drive\n"));
            // No match returns the stub.
            string none = TrayContext.FilterLogText(log, "ZZZZ");
            AssertContains(none, "no lines match filter");
        }

        private static void TestProviderCapabilitiesDefaults()
        {
            // Pixeldrain reports everything.
            ProviderCapabilities pd = ProviderCapabilities.For("pixeldrain");
            AssertTrue(pd.SupportsStorageQuota);
            AssertTrue(pd.SupportsTransferQuota);
            AssertTrue(pd.SupportsFileCount);

            // Drive has storage but no transfer-quota concept in our model.
            ProviderCapabilities drive = ProviderCapabilities.For("drive");
            AssertTrue(drive.SupportsStorageQuota);
            AssertFalse(drive.SupportsTransferQuota);
            AssertContains(drive.DefaultTransferQuotaText(), "not applicable");

            // S3 has neither — labels reflect that.
            ProviderCapabilities s3 = ProviderCapabilities.For("s3");
            AssertFalse(s3.SupportsStorageQuota);
            AssertFalse(s3.SupportsTransferQuota);
            AssertContains(s3.DefaultStorageText(), "not applicable");
            AssertContains(s3.DefaultTransferQuotaText(), "not applicable");

            // Unknown provider falls into custom defaults rather than throwing.
            ProviderCapabilities custom = ProviderCapabilities.For("madeup");
            AssertEqual("custom", custom.Provider);
            AssertTrue(custom.SupportsStorageQuota);
            AssertFalse(custom.SupportsTransferQuota);

            // Reading by remote string also resolves the provider.
            ProviderCapabilities byRemote = ProviderCapabilities.For("Pixeldrain:");
            AssertEqual("pixeldrain", byRemote.Provider);
        }

        private static void TestBuildProfilesExportJson()
        {
            RemoteProfile p = new RemoteProfile();
            p.Id = "abc123";
            p.Label = "Pixeldrain primary";
            p.Provider = "pixeldrain";
            p.Remote = "Pixeldrain:";
            p.DriveLetter = "P:";
            p.MountMode = "network";
            p.AutoMount = true;
            p.FullCache = false;
            p.BandwidthLimit = "1M";
            p.ScheduleEnabled = true;
            p.ScheduleMountTime = "09:00";
            p.ScheduleUnmountTime = "18:00";
            p.ScheduleDays = "Mon,Wed,Fri";

            string json = TrayContext.BuildProfilesExportJson(new RemoteProfile[] { p });
            AssertContains(json, "\"_pixelpipeExport\"");
            AssertContains(json, "\"version\":\"" + "0.9" + "\"");
            AssertContains(json, "\"profiles\":");
            AssertContains(json, "\"Id\":\"abc123\"");
            AssertContains(json, "\"BandwidthLimit\":\"1M\"");
            AssertContains(json, "\"ScheduleDays\":\"Mon,Wed,Fri\"");
            // Encrypted secrets must NOT leak into exports.
            AssertFalse(json.IndexOf("PixeldrainApiKeyProtected", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void TestTryParseProfilesExportJson()
        {
            // Round-trip: export → parse → check fields preserved.
            RemoteProfile p = new RemoteProfile();
            p.Id = "deadbeef";
            p.Label = "Drive personal";
            p.Provider = "drive";
            p.Remote = "Drive:";
            p.DriveLetter = "G:";
            p.AutoMount = true;
            p.ScheduleEnabled = true;
            p.ScheduleMountTime = "07:30";
            p.ScheduleDays = "Mon,Tue,Wed,Thu,Fri";

            string json = TrayContext.BuildProfilesExportJson(new RemoteProfile[] { p });
            List<RemoteProfile> parsed;
            string error;
            AssertTrue(TrayContext.TryParseProfilesExportJson(json, out parsed, out error));
            AssertEqual(1, parsed.Count);
            AssertEqual("deadbeef", parsed[0].Id);
            AssertEqual("Drive personal", parsed[0].Label);
            AssertEqual("drive", parsed[0].Provider);
            AssertEqual("Drive:", parsed[0].Remote);
            AssertEqual("G:", parsed[0].DriveLetter);
            AssertTrue(parsed[0].AutoMount);
            AssertTrue(parsed[0].ScheduleEnabled);
            AssertEqual("07:30", parsed[0].ScheduleMountTime);
            AssertEqual("Mon,Tue,Wed,Thu,Fri", parsed[0].ScheduleDays);

            // Empty/garbage inputs return false with a usable message.
            List<RemoteProfile> ignored;
            AssertFalse(TrayContext.TryParseProfilesExportJson("", out ignored, out error));
            AssertContains(error, "empty");
            AssertFalse(TrayContext.TryParseProfilesExportJson("not-json", out ignored, out error));
            // {"foo":1} is parsable JSON but missing "profiles".
            AssertFalse(TrayContext.TryParseProfilesExportJson("{\"foo\":1}", out ignored, out error));
            AssertContains(error, "profiles");

            // Legacy capitalisation: a raw settings.json with "Profiles" still imports.
            string legacy = "{\"Profiles\":[{\"Id\":\"x\",\"Label\":\"L\",\"Provider\":\"drive\",\"Remote\":\"R:\",\"DriveLetter\":\"X:\"}]}";
            AssertTrue(TrayContext.TryParseProfilesExportJson(legacy, out parsed, out error));
            AssertEqual(1, parsed.Count);
        }

        private static void TestPlanProfileImport()
        {
            RemoteProfile already = new RemoteProfile();
            already.Id = "already-here";
            already.Label = "A";
            RemoteProfile fresh = new RemoteProfile();
            fresh.Id = "brand-new";
            fresh.Label = "B";
            RemoteProfile noId = new RemoteProfile();
            noId.Id = "";
            noId.Label = "C";

            HashSet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            existing.Add("already-here");

            List<RemoteProfile> incoming = new List<RemoteProfile>();
            incoming.Add(already);
            incoming.Add(fresh);
            incoming.Add(noId);

            TrayContext.ImportPlan plan = TrayContext.PlanProfileImport(incoming, existing);
            AssertEqual(2, plan.NewProfiles.Count); // fresh + noId (treated as new)
            AssertEqual(1, plan.AlreadyPresent.Count);
            AssertEqual("already-here", plan.AlreadyPresent[0].Id);
        }

        private static void TestBuildRcloneConfigCreateArgs()
        {
            // No fields → just "config create <name> <type> --non-interactive".
            string a = TrayContext.BuildRcloneConfigCreateArgs("MyDrive", "drive", null);
            AssertEqual("config create \"MyDrive\" drive --non-interactive", a);

            // Single field, simple value.
            List<KeyValuePair<string, string>> one = new List<KeyValuePair<string, string>>();
            one.Add(new KeyValuePair<string, string>("user", "alice"));
            string b = TrayContext.BuildRcloneConfigCreateArgs("MyFTP", "ftp", one);
            AssertEqual("config create \"MyFTP\" ftp user \"alice\" --non-interactive", b);

            // Multiple fields preserve order; quoting handles whitespace and quotes.
            List<KeyValuePair<string, string>> many = new List<KeyValuePair<string, string>>();
            many.Add(new KeyValuePair<string, string>("provider", "Wasabi"));
            many.Add(new KeyValuePair<string, string>("access_key_id", "AKIA EXAMPLE"));
            many.Add(new KeyValuePair<string, string>("secret_access_key", "secret\"with\"quotes"));
            string c = TrayContext.BuildRcloneConfigCreateArgs("Cold", "s3", many);
            AssertContains(c, "config create \"Cold\" s3 ");
            AssertContains(c, "provider \"Wasabi\"");
            AssertContains(c, "access_key_id \"AKIA EXAMPLE\"");
            AssertContains(c, "secret_access_key \"secret\\\"with\\\"quotes\"");
            AssertContains(c, "--non-interactive");

            // Empty key is skipped without throwing.
            List<KeyValuePair<string, string>> withEmptyKey = new List<KeyValuePair<string, string>>();
            withEmptyKey.Add(new KeyValuePair<string, string>("", "ignored"));
            withEmptyKey.Add(new KeyValuePair<string, string>("user", "bob"));
            string d = TrayContext.BuildRcloneConfigCreateArgs("X", "sftp", withEmptyKey);
            AssertEqual("config create \"X\" sftp user \"bob\" --non-interactive", d);
        }

        private static void TestNormalizeWatchMode()
        {
            AssertEqual("move", TrayContext.NormalizeWatchMode(""));
            AssertEqual("move", TrayContext.NormalizeWatchMode(null));
            AssertEqual("move", TrayContext.NormalizeWatchMode("garbage"));
            AssertEqual("move", TrayContext.NormalizeWatchMode("MOVE"));
            AssertEqual("copy", TrayContext.NormalizeWatchMode("copy"));
            AssertEqual("copy", TrayContext.NormalizeWatchMode(" COPY "));
        }

        private static void TestBuildWatchUploadArgs()
        {
            RemoteProfile p = new RemoteProfile();
            p.Remote = "Pixeldrain:";
            p.WatchFolderMode = "move";
            p.WatchFolderTargetDir = "";

            // No target subdir: file lands at remote root.
            string a = TrayContext.BuildWatchUploadArgs(p, "C:\\Watch\\report.pdf");
            AssertEqual("moveto \"C:\\Watch\\report.pdf\" \"Pixeldrain:report.pdf\"", a);

            // Copy mode uses copyto.
            p.WatchFolderMode = "copy";
            string b = TrayContext.BuildWatchUploadArgs(p, "C:\\Watch\\notes.txt");
            AssertEqual("copyto \"C:\\Watch\\notes.txt\" \"Pixeldrain:notes.txt\"", b);

            // Subdir is joined with /; backslashes normalised; leading/trailing
            // slashes stripped so we never produce e.g. "Pixeldrain://Inbox/file".
            p.WatchFolderMode = "move";
            p.WatchFolderTargetDir = "Inbox/Reports";
            string c = TrayContext.BuildWatchUploadArgs(p, "C:\\Watch\\q3.xlsx");
            AssertEqual("moveto \"C:\\Watch\\q3.xlsx\" \"Pixeldrain:Inbox/Reports/q3.xlsx\"", c);

            p.WatchFolderTargetDir = "/Inbox\\";
            string d = TrayContext.BuildWatchUploadArgs(p, "C:\\Watch\\x.zip");
            AssertEqual("moveto \"C:\\Watch\\x.zip\" \"Pixeldrain:Inbox/x.zip\"", d);

            // Null profile doesn't throw — falls back to defaults.
            string e = TrayContext.BuildWatchUploadArgs(null, "C:\\Watch\\a.bin");
            AssertContains(e, "moveto");
            AssertContains(e, "a.bin");
        }

        private static void TestIsSecretField()
        {
            // Known secret field keys — secrets must never be argv-exposed.
            AssertTrue(TrayContext.IsSecretField("pass"));
            AssertTrue(TrayContext.IsSecretField("password"));
            AssertTrue(TrayContext.IsSecretField("api_key"));
            AssertTrue(TrayContext.IsSecretField("secret_access_key"));
            AssertTrue(TrayContext.IsSecretField("client_secret"));

            // Case-insensitive.
            AssertTrue(TrayContext.IsSecretField("PASS"));
            AssertTrue(TrayContext.IsSecretField("Api_Key"));

            // Non-secret fields should pass through on argv.
            AssertFalse(TrayContext.IsSecretField("host"));
            AssertFalse(TrayContext.IsSecretField("user"));
            AssertFalse(TrayContext.IsSecretField("provider"));
            AssertFalse(TrayContext.IsSecretField("endpoint"));
            AssertFalse(TrayContext.IsSecretField(""));
            AssertFalse(TrayContext.IsSecretField(null));
        }

        private static void TestMergeRcloneConfigSection()
        {
            // New file: just our section appears.
            List<KeyValuePair<string, string>> fields = new List<KeyValuePair<string, string>>();
            fields.Add(new KeyValuePair<string, string>("api_key", "OBSCURED-XYZ"));
            string fresh = TrayContext.MergeRcloneConfigSection("", "Pixeldrain", "pixeldrain", fields);
            AssertContains(fresh, "[Pixeldrain]");
            AssertContains(fresh, "type = pixeldrain");
            AssertContains(fresh, "api_key = OBSCURED-XYZ");

            // Existing other section preserved.
            string existing = "[OtherRemote]\r\ntype = s3\r\nprovider = AWS\r\n\r\n";
            string merged = TrayContext.MergeRcloneConfigSection(existing, "NewOne", "drive", null);
            AssertContains(merged, "[OtherRemote]");
            AssertContains(merged, "provider = AWS");
            AssertContains(merged, "[NewOne]");
            AssertContains(merged, "type = drive");

            // Replacing an existing section: only one [Pixeldrain] in output.
            string before = "[Pixeldrain]\r\ntype = pixeldrain\r\napi_key = OLD\r\n\r\n[Other]\r\ntype = drive\r\n";
            List<KeyValuePair<string, string>> replace = new List<KeyValuePair<string, string>>();
            replace.Add(new KeyValuePair<string, string>("api_key", "NEW"));
            string replaced = TrayContext.MergeRcloneConfigSection(before, "Pixeldrain", "pixeldrain", replace);
            int firstIdx = replaced.IndexOf("[Pixeldrain]", StringComparison.Ordinal);
            int lastIdx = replaced.LastIndexOf("[Pixeldrain]", StringComparison.Ordinal);
            AssertEqual(firstIdx, lastIdx); // only one occurrence
            AssertContains(replaced, "api_key = NEW");
            AssertFalse(replaced.Contains("api_key = OLD"));
            AssertContains(replaced, "[Other]");
        }

        private static void TestParseSha256ForFile()
        {
            string sums = "abc123def456  rclone-v1.71.1-windows-amd64.zip\n" +
                          "0011223344  rclone-v1.71.1-linux-amd64.zip\n" +
                          "ffff*rclone-v1.71.1-windows-arm64.zip\n";
            AssertEqual("abc123def456", TrayContext.ParseSha256ForFile(sums, "rclone-v1.71.1-windows-amd64.zip"));
            AssertEqual("0011223344", TrayContext.ParseSha256ForFile(sums, "rclone-v1.71.1-linux-amd64.zip"));
            // Case-insensitive filename match.
            AssertEqual("abc123def456", TrayContext.ParseSha256ForFile(sums, "RCLONE-V1.71.1-WINDOWS-AMD64.ZIP"));
            // No match returns empty.
            AssertEqual("", TrayContext.ParseSha256ForFile(sums, "missing.zip"));
            // Empty / null input is empty, not exception.
            AssertEqual("", TrayContext.ParseSha256ForFile("", "anything.zip"));
            AssertEqual("", TrayContext.ParseSha256ForFile(null, "anything.zip"));
            AssertEqual("", TrayContext.ParseSha256ForFile(sums, ""));
            AssertEqual("", TrayContext.ParseSha256ForFile(sums, null));
        }

        private static void TestProcessResultSucceeded()
        {
            // ExitCode 0 + no timeout + no launch error → success.
            TrayContext.ProcessResult ok = new TrayContext.ProcessResult();
            ok.ExitCode = 0;
            AssertTrue(ok.Succeeded);

            // Non-zero exit code → failure even with empty stderr (the BUG-1
            // case: rclone moveto prints nothing on success but its failure
            // exit code used to be ignored).
            TrayContext.ProcessResult exitNonZero = new TrayContext.ProcessResult();
            exitNonZero.ExitCode = 1;
            AssertFalse(exitNonZero.Succeeded);

            // Timed out → failure regardless of exit.
            TrayContext.ProcessResult timed = new TrayContext.ProcessResult();
            timed.ExitCode = 0;
            timed.TimedOut = true;
            AssertFalse(timed.Succeeded);

            // Launch error → failure.
            TrayContext.ProcessResult launch = new TrayContext.ProcessResult();
            launch.LaunchError = "rclone not found";
            AssertFalse(launch.Succeeded);

            // Combined output sums both streams.
            TrayContext.ProcessResult combined = new TrayContext.ProcessResult();
            combined.StdOut = "out";
            combined.StdErr = "err";
            AssertEqual("outerr", combined.CombinedOutput);
        }

        private static void TestParseBandwidthSchedule()
        {
            // Empty / null returns empty list.
            AssertEqual(0, TrayContext.ParseBandwidthSchedule("").Count);
            AssertEqual(0, TrayContext.ParseBandwidthSchedule(null).Count);
            AssertEqual(0, TrayContext.ParseBandwidthSchedule("   ").Count);

            // Single entry, normalised time and limit.
            List<TrayContext.BandwidthScheduleEntry> one = TrayContext.ParseBandwidthSchedule("9:00=1M");
            AssertEqual(1, one.Count);
            AssertEqual("09:00", one[0].Time);
            AssertEqual("1M", one[0].Limit);

            // Multiple entries with spaces around them.
            List<TrayContext.BandwidthScheduleEntry> multi = TrayContext.ParseBandwidthSchedule(" 00:00=off, 09:00=1M, 18:00=off ");
            AssertEqual(3, multi.Count);
            AssertEqual("00:00", multi[0].Time);
            AssertEqual("off", multi[0].Limit);
            AssertEqual("18:00", multi[2].Time);

            // Garbage tokens dropped, valid ones kept.
            List<TrayContext.BandwidthScheduleEntry> mixed = TrayContext.ParseBandwidthSchedule("garbage,09:00=1M,25:00=off,12:00=fast,18:00=10M");
            AssertEqual(2, mixed.Count);
            AssertEqual("09:00", mixed[0].Time);
            AssertEqual("18:00", mixed[1].Time);
        }

        private static void TestClassifyActivity()
        {
            // [error] level beats everything.
            AssertEqual("Error", TrayContext.ClassifyActivity("error", "mount", "boom"));

            // Area-based routing.
            AssertEqual("Mount", TrayContext.ClassifyActivity("info", "mount profile", "started"));
            AssertEqual("Unmount", TrayContext.ClassifyActivity("info", "unmount profile", "ok"));
            AssertEqual("Schedule", TrayContext.ClassifyActivity("info", "schedule mount", "Pixeldrain at 09:00"));
            AssertEqual("Watch", TrayContext.ClassifyActivity("info", "watch upload", "report.pdf"));
            AssertEqual("Orphan", TrayContext.ClassifyActivity("warn", "orphan kill", "killed pid 1234"));
            AssertEqual("Backup", TrayContext.ClassifyActivity("warn", "settings backup", "wrote"));
            AssertEqual("Update", TrayContext.ClassifyActivity("info", "update check", "newer version"));
            AssertEqual("Startup", TrayContext.ClassifyActivity("warn", "rclone job", "ready"));

            // Message-content fallback when area is generic.
            AssertEqual("Transfer", TrayContext.ClassifyActivity("info", "", "Pixeldrain: transfer finished — 12 MB moved"));

            // Plain warn with no specific area.
            AssertEqual("Warning", TrayContext.ClassifyActivity("warn", "read settings", "loaded backup"));

            // Unknown → Other.
            AssertEqual("Other", TrayContext.ClassifyActivity("info", "", "hello"));
        }

        private static void TestFormatActivityEvents()
        {
            // Empty input renders the stub message, not an empty string.
            string empty = TrayContext.FormatActivityEvents(new List<ActivityEvent>(), "All");
            AssertContains(empty, "no activity yet");

            List<ActivityEvent> evs = new List<ActivityEvent>();
            evs.Add(new ActivityEvent { Time = new DateTime(2026, 5, 28, 14, 30, 15), Category = "Mount", Message = "mount profile: Pixeldrain mounted on P:" });
            evs.Add(new ActivityEvent { Time = new DateTime(2026, 5, 28, 14, 31, 00), Category = "Transfer", Message = "Pixeldrain: transfer finished — 12 MB moved" });

            // "All" keeps everything.
            string all = TrayContext.FormatActivityEvents(evs, "All");
            AssertContains(all, "Mount");
            AssertContains(all, "Transfer");
            AssertContains(all, "2026-05-28 14:30:15");

            // Category filter keeps only matching events.
            string mountOnly = TrayContext.FormatActivityEvents(evs, "Mount");
            AssertContains(mountOnly, "Mount");
            AssertFalse(mountOnly.Contains("Transfer"));

            // No match returns explicit stub.
            string none = TrayContext.FormatActivityEvents(evs, "Backup");
            AssertContains(none, "no events match");
        }

        private static void TestParseActivityLog()
        {
            string log = "2026-05-28 14:30:15 [warn] [mount profile] Pixeldrain mounted on P:\r\n" +
                         "2026-05-28 14:31:00 [warn] [transfer] Pixeldrain: transfer finished — 12 MB moved\r\n" +
                         "garbage line that should be skipped\r\n" +
                         "2026-05-28 14:32:00 [error] [mount profile] WinFsp missing";

            List<ActivityEvent> evs = TrayContext.ParseActivityLog(log, 100);
            // Most recent first.
            AssertEqual(3, evs.Count);
            AssertEqual("Error", evs[0].Category);
            AssertEqual("Transfer", evs[1].Category);
            AssertEqual("Mount", evs[2].Category);
            // Cap honored.
            AssertEqual(2, TrayContext.ParseActivityLog(log, 2).Count);
            // Empty input returns empty list, not null.
            AssertEqual(0, TrayContext.ParseActivityLog("", 100).Count);
            AssertEqual(0, TrayContext.ParseActivityLog(null, 100).Count);
        }

        private static void TestCommandLineMentionsDrive()
        {
            // The mount argument quoting in QuoteArg always wraps the drive in
            // double quotes, so the primary pattern to match is `"P:"`.
            string typical = "C:\\Apps\\rclone\\rclone.exe mount \"Pixeldrain:\" \"P:\" --links --vfs-cache-mode writes";
            AssertTrue(TrayContext.CommandLineMentionsDrive(typical, "P:"));
            AssertFalse(TrayContext.CommandLineMentionsDrive(typical, "Q:"));

            // Trailing backslash form (Pixelpipe sometimes hands rclone P:\).
            string trailing = "rclone mount \"Pixeldrain:\" \"P:\\\" --links";
            AssertTrue(TrayContext.CommandLineMentionsDrive(trailing, "P:"));

            // Unquoted with whitespace boundary.
            string unquoted = "rclone mount Pixeldrain: P: --links";
            AssertTrue(TrayContext.CommandLineMentionsDrive(unquoted, "P:"));

            // Must NOT match remote-name colons (`"Pixeldrain:"` should not
            // count as mentioning drive `P:` or `n:`).
            AssertFalse(TrayContext.CommandLineMentionsDrive("rclone mount \"Pixeldrain:\" \"X:\" --links", "P:"));
            AssertFalse(TrayContext.CommandLineMentionsDrive("rclone mount \"Pixeldrain:\" \"X:\" --links", "n:"));

            // Empty / null inputs are false, not exceptions.
            AssertFalse(TrayContext.CommandLineMentionsDrive("", "P:"));
            AssertFalse(TrayContext.CommandLineMentionsDrive(null, "P:"));
            AssertFalse(TrayContext.CommandLineMentionsDrive("rclone mount", ""));
            AssertFalse(TrayContext.CommandLineMentionsDrive("rclone mount", null));

            // Case-insensitive.
            AssertTrue(TrayContext.CommandLineMentionsDrive("rclone mount \"P:\" --links", "p:"));
        }

        private static void TestComputeWatchNextRetryUtc()
        {
            DateTime t0 = new DateTime(2026, 5, 28, 12, 0, 0, DateTimeKind.Utc);
            // First attempt → 30s back-off.
            AssertEqual(t0.AddSeconds(30), TrayContext.ComputeWatchNextRetryUtc(1, t0));
            // Second → 120s.
            AssertEqual(t0.AddSeconds(120), TrayContext.ComputeWatchNextRetryUtc(2, t0));
            // Third → 600s.
            AssertEqual(t0.AddSeconds(600), TrayContext.ComputeWatchNextRetryUtc(3, t0));
            // Beyond the array clamps to the last value rather than running forever.
            AssertEqual(t0.AddSeconds(600), TrayContext.ComputeWatchNextRetryUtc(99, t0));
            // Zero / negative attempts are treated as the first try.
            AssertEqual(t0.AddSeconds(30), TrayContext.ComputeWatchNextRetryUtc(0, t0));
            AssertEqual(t0.AddSeconds(30), TrayContext.ComputeWatchNextRetryUtc(-3, t0));
        }

        private static void AssertEqual(object expected, object actual)
        {
            bool equal;
            if (expected == null) equal = actual == null;
            else equal = expected.Equals(actual);
            if (!equal) throw new AssertionException("expected <" + (expected == null ? "null" : expected.ToString()) + "> got <" + (actual == null ? "null" : actual.ToString()) + ">");
        }

        private static void AssertTrue(bool condition)
        {
            if (!condition) throw new AssertionException("expected true got false");
        }

        private static void AssertFalse(bool condition)
        {
            if (condition) throw new AssertionException("expected false got true");
        }

        private static void AssertContains(string haystack, string needle)
        {
            if (haystack == null || !haystack.Contains(needle)) throw new AssertionException("expected <" + haystack + "> to contain <" + needle + ">");
        }

        private sealed class AssertionException : Exception
        {
            public AssertionException(string message) : base(message) { }
        }
    }
}
