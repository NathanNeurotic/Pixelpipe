using System;
using System.Collections.Generic;

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
            Run("TrayMenuPlacement", TestTrayMenuPlacement);
            Run("HasArg", TestHasArg);
            Run("ProfilePortFor", TestProfilePortFor);
            Run("IsValidBandwidth", TestIsValidBandwidth);
            Run("ScrubSecrets", TestScrubSecrets);
            Run("BoxProvider", TestBoxProvider);

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
