using System;
using System.Drawing;

namespace FastDM
{
    internal static class ThemeContrastPolicy
    {
        public static readonly Color LightWindow = Color.FromArgb(255, 255, 255);
        public static readonly Color LightText = Color.FromArgb(45, 49, 66);
        public static readonly Color LightSubText = Color.FromArgb(107, 112, 137);
        public static readonly Color LightToolbar = Color.FromArgb(255, 255, 255);
        public static readonly Color LightButton = Color.FromArgb(244, 245, 250);
        public static readonly Color LightDisabledText = Color.FromArgb(117, 117, 117);
        public static readonly Color DarkWindow = Color.FromArgb(23, 25, 35);
        public static readonly Color DarkText = Color.FromArgb(230, 232, 242);
        public static readonly Color DarkSubText = Color.FromArgb(154, 160, 180);
        public static readonly Color DarkToolbar = Color.FromArgb(31, 34, 48);
        public static readonly Color DarkButton = Color.FromArgb(38, 42, 59);
        public static readonly Color DarkDisabledText = Color.FromArgb(154, 160, 180);

        public static double ContrastRatio(Color foreground, Color background)
        {
            static double Linear(byte value)
            {
                double channel = value / 255.0;
                return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
            }
            double a = 0.2126 * Linear(foreground.R) + 0.7152 * Linear(foreground.G) + 0.0722 * Linear(foreground.B);
            double b = 0.2126 * Linear(background.R) + 0.7152 * Linear(background.G) + 0.0722 * Linear(background.B);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }
    }
}
