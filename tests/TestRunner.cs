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
            AssertEqual("unknown", TrayContext.FormatBytesValue(-1));
            AssertEqual("0 B", TrayContext.FormatBytesValue(0));
            AssertEqual("512 B", TrayContext.FormatBytesValue(512));
            AssertEqual("1 KB", TrayContext.FormatBytesValue(1024));
            AssertEqual("1.5 KB", TrayContext.FormatBytesValue(1536));
            AssertEqual("1 MB", TrayContext.FormatBytesValue(1024L * 1024));
            AssertEqual("1 GB", TrayContext.FormatBytesValue(1024L * 1024 * 1024));
            AssertEqual("1 TB", TrayContext.FormatBytesValue(1024L * 1024 * 1024 * 1024));
        }

        private static void TestDisplayLimit()
        {
            AssertEqual("Unlimited", TrayContext.DisplayLimitValue("off"));
            AssertEqual("Unlimited", TrayContext.DisplayLimitValue("OFF"));
            AssertEqual("Unlimited", TrayContext.DisplayLimitValue(""));
            AssertEqual("Unlimited", TrayContext.DisplayLimitValue(null));
            AssertEqual("1M/s", TrayContext.DisplayLimitValue("1M"));
            AssertEqual("512K/s", TrayContext.DisplayLimitValue("512K"));
        }

        private static void TestNormalizeDriveLetter()
        {
            AssertEqual("P:", TrayContext.NormalizeDriveLetterValue("p"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetterValue("P"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetterValue("P:"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetterValue("p:"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetterValue("P:\\"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetterValue("p:\\foo\\bar"));
            AssertEqual("Z:", TrayContext.NormalizeDriveLetterValue("z"));
            AssertEqual("P:", TrayContext.NormalizeDriveLetterValue(""));
            AssertEqual("P:", TrayContext.NormalizeDriveLetterValue(null));
            AssertEqual("P:", TrayContext.NormalizeDriveLetterValue("garbage"));
        }

        private static void TestNormalizeRemoteName()
        {
            AssertEqual("foo:", TrayContext.NormalizeRemoteNameValue("foo"));
            AssertEqual("foo:", TrayContext.NormalizeRemoteNameValue("foo:"));
            AssertEqual("Pixeldrain:", TrayContext.NormalizeRemoteNameValue("Pixeldrain"));
            AssertEqual("Pixeldrain:", TrayContext.NormalizeRemoteNameValue(""));
            AssertEqual("Pixeldrain:", TrayContext.NormalizeRemoteNameValue(null));
            AssertEqual("Pixeldrain:", TrayContext.NormalizeRemoteNameValue("   "));
        }

        private static void TestRemoteNameBare()
        {
            AssertEqual("foo", TrayContext.RemoteNameBareValue("foo:"));
            AssertEqual("foo", TrayContext.RemoteNameBareValue("foo"));
            AssertEqual("Pixeldrain", TrayContext.RemoteNameBareValue("Pixeldrain:"));
            AssertEqual("Pixeldrain", TrayContext.RemoteNameBareValue(null));
        }

        private static void TestNormalizeMountMode()
        {
            AssertEqual("fixed", TrayContext.NormalizeMountModeValue("fixed"));
            AssertEqual("fixed", TrayContext.NormalizeMountModeValue("FIXED"));
            AssertEqual("network", TrayContext.NormalizeMountModeValue("network"));
            AssertEqual("network", TrayContext.NormalizeMountModeValue("NETWORK"));
            AssertEqual("network", TrayContext.NormalizeMountModeValue(""));
            AssertEqual("network", TrayContext.NormalizeMountModeValue(null));
            AssertEqual("network", TrayContext.NormalizeMountModeValue("garbage"));
        }

        private static void TestNormalizeProvider()
        {
            AssertEqual("pixeldrain", TrayContext.NormalizeProviderValue("pixeldrain", ""));
            AssertEqual("pixeldrain", TrayContext.NormalizeProviderValue("Pixeldrain", ""));
            AssertEqual("pixeldrain", TrayContext.NormalizeProviderValue("", "Pixeldrain:"));
            AssertEqual("drive", TrayContext.NormalizeProviderValue("drive", ""));
            AssertEqual("drive", TrayContext.NormalizeProviderValue("google", ""));
            AssertEqual("mega", TrayContext.NormalizeProviderValue("mega", ""));
            AssertEqual("onedrive", TrayContext.NormalizeProviderValue("onedrive", ""));
            AssertEqual("dropbox", TrayContext.NormalizeProviderValue("dropbox", ""));
            AssertEqual("box", TrayContext.NormalizeProviderValue("box", ""));
            AssertEqual("s3", TrayContext.NormalizeProviderValue("s3", ""));
            AssertEqual("s3", TrayContext.NormalizeProviderValue("b2", ""));
            AssertEqual("s3", TrayContext.NormalizeProviderValue("r2", ""));
            AssertEqual("s3", TrayContext.NormalizeProviderValue("wasabi", ""));
            AssertEqual("webdav", TrayContext.NormalizeProviderValue("webdav", ""));
            AssertEqual("webdav", TrayContext.NormalizeProviderValue("nextcloud", ""));
            AssertEqual("sftp", TrayContext.NormalizeProviderValue("sftp", ""));
            AssertEqual("ftp", TrayContext.NormalizeProviderValue("ftp", ""));
            AssertEqual("custom", TrayContext.NormalizeProviderValue("", ""));
            AssertEqual("xyz", TrayContext.NormalizeProviderValue("xyz", ""));
        }

        private static void TestDisplayProvider()
        {
            AssertEqual("Pixeldrain", TrayContext.DisplayProviderValue("pixeldrain"));
            AssertEqual("Google Drive", TrayContext.DisplayProviderValue("drive"));
            AssertEqual("MEGA", TrayContext.DisplayProviderValue("mega"));
            AssertEqual("OneDrive", TrayContext.DisplayProviderValue("onedrive"));
            AssertEqual("Dropbox", TrayContext.DisplayProviderValue("dropbox"));
            AssertEqual("Box", TrayContext.DisplayProviderValue("box"));
            AssertEqual("S3-compatible", TrayContext.DisplayProviderValue("s3"));
            AssertEqual("WebDAV", TrayContext.DisplayProviderValue("webdav"));
            AssertEqual("SFTP", TrayContext.DisplayProviderValue("sftp"));
            AssertEqual("FTP", TrayContext.DisplayProviderValue("ftp"));
            AssertEqual("Custom", TrayContext.DisplayProviderValue("xyz"));
            AssertEqual("Custom", TrayContext.DisplayProviderValue(""));
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
