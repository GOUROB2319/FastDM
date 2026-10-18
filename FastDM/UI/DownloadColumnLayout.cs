using System;
using System.Linq;

namespace FastDM
{
    // Keeps the download list usable at narrow widths while restoring every column when space returns.
    internal static class DownloadColumnLayout
    {
        static readonly int[] MinimumWidths = { 140, 64, 112, 96, 75, 60, 100 };

        public static int[] Fit(int availableWidth)
        {
            var widths = new int[MinimumWidths.Length];
            if (availableWidth <= 0) return widths;

            var visible = Enumerable.Repeat(true, MinimumWidths.Length).ToArray();
            int required = MinimumWidths.Sum();

            // Hide lower priority columns first. Their full values remain available in row tooltips and Details.
            foreach (int index in new[] { 6, 5, 4, 3, 1 })
            {
                if (required <= availableWidth) break;
                visible[index] = false;
                required -= MinimumWidths[index];
            }

            if (required > availableWidth)
            {
                widths[0] = Math.Max(0, availableWidth - MinimumWidths[2]);
                widths[2] = Math.Min(availableWidth, MinimumWidths[2]);
                return widths;
            }

            for (int i = 0; i < widths.Length; i++)
                widths[i] = visible[i] ? MinimumWidths[i] : 0;

            int spare = availableWidth - required;
            AddWidth(widths, 0, ref spare, 190);
            AddWidth(widths, 2, ref spare, 68);
            AddWidth(widths, 3, ref spare, 16);
            AddWidth(widths, 6, ref spare, 45);
            AddWidth(widths, 0, ref spare, int.MaxValue);
            return widths;
        }

        static void AddWidth(int[] widths, int index, ref int spare, int maximumExtra)
        {
            if (spare <= 0 || widths[index] == 0) return;
            int extra = Math.Min(spare, maximumExtra);
            widths[index] += extra;
            spare -= extra;
        }
    }
}
