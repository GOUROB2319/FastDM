namespace FastDM
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // একটাই ইনস্ট্যান্স চলবে। দ্বিতীয়বার চালালে প্রথম উইন্ডোটা সামনে আসবে
            // (ট্রে-তে লুকিয়ে থাকলেও)।
            using var mutex = new Mutex(true, @"Local\FastDM.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                try
                {
                    using var ev = EventWaitHandle.OpenExisting(@"Local\FastDM.Show");
                    ev.Set();
                }
                catch { }
                return;
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}
