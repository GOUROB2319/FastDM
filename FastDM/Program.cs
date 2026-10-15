using System.IO.Pipes;
using System.Text;

namespace FastDM
{
    internal static class Program
    {
        // fastdm:// launch command is stored here for the first app instance.
        internal static string StartupCommand = null!;

        // Windows চালু হওয়ার সময় নিজে থেকে শুরু হলে true (ট্রে-তে লুকিয়ে থাকবে)
        internal static bool StartMinimized;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            string? cmd = args.FirstOrDefault(
                a => a.StartsWith("fastdm:", StringComparison.OrdinalIgnoreCase));

            // Only one application instance is allowed.
            // If another instance starts with a fastdm:// command, forward it
            // to the first instance through the named pipe.
            using var mutex = new Mutex(
                true,
                @"Local\FastDM.SingleInstance",
                out bool isFirst);

            if (!isFirst)
            {
                if (cmd != null && TrySendToFirstInstance(cmd))
                {
                    return;
                }

                try
                {
                    using var ev = EventWaitHandle.OpenExisting(
                        @"Local\FastDM.Show");

                    ev.Set();
                }
                catch (Exception exIgnored1) {
                    // Ignore if the first instance is shutting down.
                    AppLog.Write("Program (ignored): " + exIgnored1.Message);
                }

                return;
            }

            // Keep the existing null behavior while satisfying nullable analysis.
            StartupCommand = cmd!;

            // Portable build: the Run key starts us with --minimized.
            // Store build: the app was activated by its StartupTask.
            StartMinimized =
                args.Any(a => a.Equals(StartupManager.MinimizedArg, StringComparison.OrdinalIgnoreCase)) ||
                (UpdateChecker.IsPackaged && StartupManager.LaunchedByStartupTask());

            // Last line of defence: an unexpected error is shown and logged
            // instead of silently killing the app (e.g. inside an async void handler).
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                AppLog.Write("Unobserved task error: " + e.Exception.GetBaseException());
                e.SetObserved();
            };

            // Configure the Windows Forms application.
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }

        // Unhandled error from anywhere: written to the log, then shown to the user.
        static void ReportCrash(Exception? ex)
        {
            AppLog.Write("Unhandled error: " + ex);
            ShowError(ex);
        }

        // Error caught by a guarded event handler (async void methods and handlers).
        internal static void ReportError(string where, Exception ex)
        {
            AppLog.Write(where + " failed: " + ex);
            ShowError(ex);
        }

        static void ShowError(Exception? ex)
        {
            try
            {
                MessageBox.Show(
                    "FastDM hit an unexpected error:\n\n" + (ex?.Message ?? "unknown error"),
                    "FastDM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // Nothing more can be done here.
            }
        }

        static bool TrySendToFirstInstance(string cmd)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    ProtocolHandler.PipeName(),
                    PipeDirection.Out);

                client.Connect(1500);

                using var writer = new StreamWriter(
                    client,
                    new UTF8Encoding(false))
                {
                    AutoFlush = true
                };

                writer.WriteLine(cmd);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
