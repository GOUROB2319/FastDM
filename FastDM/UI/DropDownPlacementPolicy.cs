using System;
using System.Drawing;

namespace FastDM
{
    internal static class DropDownPlacementPolicy
    {
        public static int MaximumHeight(int workAreaHeight) =>
            Math.Max(1, (int)Math.Floor(Math.Max(1, workAreaHeight) * 0.4));

        public static Rectangle Fit(Rectangle popup, Rectangle workArea, int anchorTop)
        {
            int width = Math.Clamp(popup.Width, 1, Math.Max(1, workArea.Width));
            int height = Math.Clamp(popup.Height, 1, MaximumHeight(workArea.Height));
            int x = Math.Clamp(popup.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
            int y = popup.Top;
            if (y + height > workArea.Bottom) y = anchorTop - height;
            y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
            return new Rectangle(x, y, width, height);
        }
    }
}
