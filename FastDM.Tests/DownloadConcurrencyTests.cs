using FastDM;

namespace FastDM.Tests
{
    static class DownloadConcurrencyTests
    {
        public static void Run()
        {
            T.Section("Download queue concurrency policy");
            T.Check("new High mode permits four simultaneous downloads", DownloadQueuePolicy.AvailableSlots(0, 4) == 4);
            T.Check("cap one leaves only one runnable queue slot", DownloadQueuePolicy.AvailableSlots(0, 1) == 1);
            T.Check("active jobs consume configured slots", DownloadQueuePolicy.AvailableSlots(3, 4) == 1);
            T.Check("active count at the configured cap starts nothing", DownloadQueuePolicy.AvailableSlots(4, 4) == 0);
            T.Check("invalid queue settings are clamped safely", DownloadQueuePolicy.AvailableSlots(-2, 99) == 10);
        }
    }
}
