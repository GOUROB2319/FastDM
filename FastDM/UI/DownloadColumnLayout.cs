using System;
using System.Linq;

namespace FastDM
{
    // Keeps the download list usable at narrow widths while restoring every column when space returns.
    internal static class DownloadColumnLayout
    {
        static readonly int[] LogicalMinimumWidths = { 140, 84, 112, 96, 100, 76, 100 };
        static readonly int[] RightAligned = { 1, 4, 5 };

        public static bool IsRightAligned(int columnIndex) => RightAligned.Contains(columnIndex);

        public static int[] Fit(int availableWidth, int dpi = 96)
        {
            var minimumWidths = LogicalMinimumWidths
                .Select(width => Math.Max(1, (int)Math.Round(width * dpi / 96.0)))
                .ToArray();
            var widths = new int[minimumWidths.Length];
            if (availableWidth <= 0) return widths;

            var visible = Enumerable.Repeat(true, minimumWidths.Length).ToArray();
            int required = minimumWidths.Sum();

            // Hide lower priority columns first. Their full values remain available in row tooltips and Details.
            foreach (int index in new[] { 6, 5, 4, 3, 1 })
            {
                if (required <= availableWidth) break;
                visible[index] = false;
                required -= minimumWidths[index];
            }

            if (required > availableWidth)
            {
                widths[0] = Math.Max(0, availableWidth - minimumWidths[2]);
                widths[2] = Math.Min(availableWidth, minimumWidths[2]);
                return widths;
            }

            for (int i = 0; i < widths.Length; i++)
                widths[i] = visible[i] ? minimumWidths[i] : 0;

            int spare = availableWidth - required;
            AddWidth(widths, 0, ref spare, 130);
            AddWidth(widths, 2, ref spare, 62);
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
