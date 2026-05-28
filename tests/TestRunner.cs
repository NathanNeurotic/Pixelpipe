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
            Run("ScrubSecrets", TestScrubSecrets);
            Run("BoxProvider", TestBoxProvider);
            Run("ParseBytesPerSec", TestParseBytesPerSec);
            Run("ParseBytes", TestParseBytes);
            Run("ParseStoragePercent", TestParseStoragePercent);

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
