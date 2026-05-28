using System;
using System.Net;
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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayContext(args));
        }

        internal static bool HasArg(string[] args, string wanted)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++) if (String.Equals(args[i], wanted, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
