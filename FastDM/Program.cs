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
                catch
                {
                    // Ignore if the first instance is shutting down.
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

            // Configure the Windows Forms application.
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
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
