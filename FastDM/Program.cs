using System.IO.Pipes;
using System.Text;

namespace FastDM
{
    internal static class Program
    {
        // fastdm:// দিয়ে অ্যাপ চালু হলে সেই লিঙ্ক এখানে থাকে (Form1 স্টার্টআপের পর সামলায়)
        internal static string StartupCommand;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            string cmd = args.FirstOrDefault(a => a.StartsWith("fastdm:", StringComparison.OrdinalIgnoreCase));

            // একটাই ইনস্ট্যান্স চলবে। দ্বিতীয়বার চালালে প্রথম উইন্ডোটা সামনে আসবে
            // (ট্রে-তে লুকিয়ে থাকলেও), আর fastdm:// লিঙ্ক থাকলে সেটা প্রথম ইনস্ট্যান্সে পাঠানো হবে।
            using var mutex = new Mutex(true, @"Local\FastDM.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                if (cmd != null && TrySendToFirstInstance(cmd)) return;
                try
                {
                    using var ev = EventWaitHandle.OpenExisting(@"Local\FastDM.Show");
                    ev.Set();
                }
                catch { }
                return;
            }

            StartupCommand = cmd;

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }

        static bool TrySendToFirstInstance(string cmd)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", ProtocolHandler.PipeName(), PipeDirection.Out);
                client.Connect(1500);
                using var w = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                w.WriteLine(cmd);
                return true;
            }
            catch { return false; }
        }
    }
}
