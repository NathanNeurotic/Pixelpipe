using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal static class Program
    {
        private static void ConfigureModernTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.DefaultConnectionLimit = 16;
            }
            catch { }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            ConfigureModernTls();
            if (HasArg(args, "/smoketest-menu"))
            {
                Environment.ExitCode = TrayMenuPlacementSmokeTest.Run();
                return;
            }

            bool createdNew;
            Mutex singleInstance = new Mutex(true, @"Local\Pixelpipe.TrayApp", out createdNew);
            try
            {
                if (!createdNew)
                {
                    // Existing instance might be healthy OR hung. Try the
                    // wake-pipe first; if it answers within 2 s we yield to
                    // it. If it doesn't, the holder is hung — terminate any
                    // other Pixelpipe.exe and re-acquire the mutex.
                    bool woke = SingleInstanceChannel.TryWakeExisting();
                    if (woke)
                    {
                        if (!HasArg(args, "/automount"))
                        {
                            // Existing instance acked; it should already be
                            // bringing the main window forward. Quiet exit.
                        }
                        return;
                    }
                    // Hung holder. Kill it and retry the mutex.
                    int killed = SingleInstanceChannel.TerminateOtherInstances();
                    singleInstance.Dispose();
                    singleInstance = new Mutex(true, @"Local\Pixelpipe.TrayApp", out createdNew);
                    if (!createdNew)
                    {
                        MessageBox.Show("Pixelpipe could not start because another instance is holding the single-instance lock and did not respond. " + killed + " other process" + (killed == 1 ? "" : "es") + " were terminated; please try again.", "Pixelpipe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                // Trap any exception that would otherwise terminate the process. The default
                // .NET WinForms handler shows a dialog and tears the app down; for a tray
                // app that means the icon vanishes and the user thinks Pixelpipe "randomly
                // closed". Log everything to pixelpipe-ui.log and keep running where we can.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    LogCrash("UI thread exception", e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    LogCrash("AppDomain unhandled" + (e.IsTerminating ? " (terminating)" : ""), e.ExceptionObject as Exception);
                };

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext(args));
                GC.KeepAlive(singleInstance);
            }
            finally
            {
                try { SingleInstanceChannel.StopServer(); } catch { }
                try { if (singleInstance != null) singleInstance.Dispose(); } catch { }
            }
        }

        internal static bool HasArg(string[] args, string wanted)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++) if (String.Equals(args[i], wanted, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Use the same on-disk log location as TrayContext.LogUiIssue so users only have
        // one place to look. Direct-write here instead of routing through TrayContext
        // because TrayContext may itself be the source of the exception.
        private static void LogCrash(string area, Exception ex)
        {
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixelpipe", "logs");
                Directory.CreateDirectory(logDir);
                string path = Path.Combine(logDir, "pixelpipe-ui.log");
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [error] [" + area + "] " +
                              (ex == null ? "(null exception)" : ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace) +
                              Environment.NewLine;
                File.AppendAllText(path, line);
            }
            catch { }
        }
    }
}
