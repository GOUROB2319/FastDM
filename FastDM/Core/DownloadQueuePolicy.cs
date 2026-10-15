using System;

namespace FastDM
{
    // Shared queue-slot calculation used by the UI scheduler and deterministic integration tests.
    public static class DownloadQueuePolicy
    {
        public static int AvailableSlots(int active, int configuredMaximum)
        {
            int maximum = Math.Clamp(configuredMaximum, 1, 10);
            return Math.Max(0, maximum - Math.Max(0, active));
        }
    }
}
