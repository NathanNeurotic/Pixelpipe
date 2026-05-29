using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Pixelpipe
{
    internal sealed partial class TrayContext
    {
        // 30 s UI-thread heartbeat. The line goes through LogUiInfo which the
        // Activity tab parses, so a freeze leaves a visible "the heartbeats
        // stopped at 14:32" gap. Cheap to write — one line every 30 s — and
        // costs nothing if everything is healthy.
        private System.Windows.Forms.Timer heartbeatTimer;
        private DateTime lastHeartbeatUtc;
        // Refresh deadman: if a refresh worker doesn't reset `refreshingFlag`
        // within this window something is wrong (worker thread died, BeginUi
        // queue stalled, etc). The watchdog clears the flag so subsequent
        // refreshes aren't silently swallowed for the rest of the session.
        private DateTime refreshStartedUtc;
        private const int RefreshDeadmanSeconds = 90;

        private void StartLivenessTimers()
        {
            try
            {
                if (heartbeatTimer != null) return;
                heartbeatTimer = new System.Windows.Forms.Timer();
                heartbeatTimer.Interval = 30000;
                heartbeatTimer.Tick += delegate { OnHeartbeatTick(); };
                heartbeatTimer.Start();
                // First heartbeat right away so the "Pixelpipe started" event
                // shows up in the Activity tab before the first 30 s tick.
                OnHeartbeatTick();
            }
            catch (Exception ex) { LogUiIssue("liveness timers", ex); }
        }

        private void OnHeartbeatTick()
        {
            try
            {
                lastHeartbeatUtc = DateTime.UtcNow;
                LogUiInfo("heartbeat", "ui thread responsive");
                // Refresh deadman: if a refresh has been running too long,
                // the worker probably died or its BeginUi back-edge didn't
                // fire. Reset the flag so subsequent refresh requests can run.
                if (refreshingFlag != 0)
                {
                    double elapsed = (DateTime.UtcNow - refreshStartedUtc).TotalSeconds;
                    if (elapsed > RefreshDeadmanSeconds)
                    {
                        LogUiWarn("refresh deadman", "force-reset after " + elapsed.ToString("0") + "s of refreshingFlag=1");
                        Interlocked.Exchange(ref refreshingFlag, 0);
                    }
                }
            }
            catch (Exception ex) { LogUiIssue("heartbeat", ex); }
        }

        // Tools / diagnostics → "Test UI responsiveness". Measures round-trip
        // through BeginUi so the user can verify the UI thread is alive when
        // they suspect a freeze.
        private void TestUiResponsiveness()
        {
            DateTime started = DateTime.UtcNow;
            ManualResetEvent done = new ManualResetEvent(false);
            BeginUi(delegate { done.Set(); });
            bool signalled = done.WaitOne(5000);
            double elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
            string msg = signalled
                ? "UI thread responded in " + elapsed.ToString("0") + " ms."
                : "UI thread did NOT respond within 5 s. Pixelpipe may be frozen.";
            MessageBox.Show(msg, "Pixelpipe", MessageBoxButtons.OK, signalled ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }

    // Single-instance protocol via a named pipe. The first Pixelpipe to
    // launch creates the mutex AND starts a pipe server. A second launch
    // tries to connect to the pipe — if the server responds within 2 s the
    // existing instance is healthy, the new one tells it to show the main
    // window and exits. If the connect times out the existing process is
    // hung, the new one terminates it and takes over the mutex.
    //
    // Without this safeguard the previous behaviour was: new launch saw the
    // mutex was held, showed "Pixelpipe is already running", and exited —
    // which is wrong if the holder is wedged.
    internal static class SingleInstanceChannel
    {
        private const string PipeName = "Pixelpipe.TrayApp.WakePipe";
        private const string WakeRequest = "WAKE";
        private const string WakeAck = "OK";
        private const int ConnectTimeoutMs = 2000;
        private const int ReadWriteTimeoutMs = 1500;

        private static Thread serverThread;
        private static volatile bool serverShouldRun;
        private static Action onWake;

        public static void StartServer(Action onWakeCallback, Action<string, Exception> logIssue)
        {
            if (serverThread != null) return;
            onWake = onWakeCallback;
            serverShouldRun = true;
            serverThread = new Thread(delegate () { ServerLoop(logIssue); });
            serverThread.IsBackground = true;
            serverThread.Name = "Pixelpipe.WakeServer";
            serverThread.Start();
        }

        public static void StopServer()
        {
            serverShouldRun = false;
            // Pipe server will exit on its next loop iteration. We don't
            // join — the thread is background and the OS will reclaim it.
        }

        private static void ServerLoop(Action<string, Exception> logIssue)
        {
            while (serverShouldRun)
            {
                try
                {
                    using (NamedPipeServerStream server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous))
                    {
                        IAsyncResult ar = server.BeginWaitForConnection(null, null);
                        // Poll every 500 ms so a Stop can break the loop
                        // without leaving the pipe wait blocking forever.
                        while (serverShouldRun && !ar.IsCompleted) Thread.Sleep(500);
                        if (!serverShouldRun) return;
                        server.EndWaitForConnection(ar);

                        using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 256, true))
                        using (StreamWriter writer = new StreamWriter(server, Encoding.UTF8, 256, true))
                        {
                            string cmd = reader.ReadLine();
                            if (String.Equals(cmd, WakeRequest, StringComparison.OrdinalIgnoreCase))
                            {
                                Action cb = onWake;
                                if (cb != null) { try { cb(); } catch { } }
                            }
                            writer.WriteLine(WakeAck);
                            writer.Flush();
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (logIssue != null) logIssue("wake server", ex);
                    Thread.Sleep(500); // back off briefly to avoid a tight error loop
                }
            }
        }

        // Called from Program.Main when the mutex is already held. Returns
        // true if the existing instance responded (so the new launch can
        // exit silently). Returns false if the existing instance is hung —
        // caller terminates it and takes over.
        public static bool TryWakeExisting()
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    client.Connect(ConnectTimeoutMs);
                    client.ReadTimeout = ReadWriteTimeoutMs;
                    client.WriteTimeout = ReadWriteTimeoutMs;
                    using (StreamReader reader = new StreamReader(client, Encoding.UTF8, false, 256, true))
                    using (StreamWriter writer = new StreamWriter(client, Encoding.UTF8, 256, true))
                    {
                        writer.WriteLine(WakeRequest);
                        writer.Flush();
                        string ack = reader.ReadLine();
                        return String.Equals(ack, WakeAck, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
                // Any failure (timeout, broken pipe, no server) means the
                // existing instance can't be talked to. Caller treats as hung.
                return false;
            }
        }

        // If the existing instance is hung, kill every other Pixelpipe process
        // whose main module path matches our own install location. SEC-4
        // (v0.13.1): scope this by image path so an unrelated process that
        // happens to be named Pixelpipe.exe (a developer's build elsewhere,
        // a malware sample with a matching name) cannot be killed by us.
        // Returns the number of processes terminated.
        public static int TerminateOtherInstances()
        {
            int killed = 0;
            try
            {
                Process self = Process.GetCurrentProcess();
                string selfPath = "";
                try { selfPath = self.MainModule.FileName ?? ""; } catch { }
                Process[] all = Process.GetProcessesByName("Pixelpipe");
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].Id == self.Id) continue;
                    try
                    {
                        // Compare image paths case-insensitively. If we can't
                        // read the candidate's MainModule (e.g. access denied
                        // because it's a 32-bit process from a 64-bit
                        // inspector, or vice-versa), skip rather than risk
                        // killing the wrong process.
                        string otherPath = "";
                        try { otherPath = all[i].MainModule.FileName ?? ""; } catch { continue; }
                        if (String.IsNullOrEmpty(otherPath)) continue;
                        if (!String.Equals(selfPath, otherPath, StringComparison.OrdinalIgnoreCase)) continue;
                        all[i].Kill();
                        all[i].WaitForExit(3000);
                        killed++;
                    }
                    catch { }
                }
            }
            catch { }
            return killed;
        }
    }
}
