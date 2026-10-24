using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FastDM
{
    class ThemedMarqueeProgress : Control
    {
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 };
        int offset;

        public ThemedMarqueeProgress()
        {
            DoubleBuffered = true;
            Height = 8;
            timer.Tick += (s, e) =>
            {
                offset = (offset + Math.Max(2, Theme.Scale(this, 5))) % Math.Max(1, Width + Theme.Scale(this, 52));
                Invalidate();
            };
            HandleCreated += (s, e) => { if (Visible) timer.Start(); };
            HandleDestroyed += (s, e) => timer.Stop();
            VisibleChanged += (s, e) =>
            {
                if (!IsHandleCreated) return;
                if (Visible) timer.Start(); else timer.Stop();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds = ClientRectangle;
            bounds.Inflate(-1, -1);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var track = new SolidBrush(Theme.P.Track))
                e.Graphics.FillRoundedRectangle(track, bounds, bounds.Height / 2);

            int segmentWidth = Math.Min(bounds.Width, Math.Max(Theme.Scale(this, 42), bounds.Width / 3));
            int x = bounds.X + offset - segmentWidth;
            var segment = new Rectangle(x, bounds.Y, segmentWidth, bounds.Height);
            segment.Intersect(bounds);
            if (segment.Width > 0)
            {
                using var accent = new SolidBrush(Theme.P.Accent);
                e.Graphics.FillRoundedRectangle(accent, segment, segment.Height / 2);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
        {
            if (radius <= 1)
            {
                graphics.FillRectangle(brush, bounds);
                return;
            }
            int diameter = radius * 2;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            graphics.FillPath(brush, path);
        }
    }

    class ThumbnailSkeleton : Control
    {
        bool loading = true;

        public ThumbnailSkeleton()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            AccessibleName = "Thumbnail loading placeholder";
            AccessibleDescription = "The video thumbnail is loading.";
        }

        public void SetLoading(bool value)
        {
            if (loading == value) return;
            loading = value;
            AccessibleName = loading ? "Thumbnail loading placeholder" : "Thumbnail unavailable";
            AccessibleDescription = loading
                ? "The video thumbnail is loading."
                : "The thumbnail could not be retrieved. Video details and download options are still available.";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Theme.P.Hover);

            if (loading)
            {
                int inset = Theme.Scale(this, 12);
                int barHeight = Math.Max(3, Theme.Scale(this, 5));
                using var brush = new SolidBrush(Theme.P.Border);
                e.Graphics.FillRectangle(brush, inset, inset, Math.Max(1, Width - inset * 2), barHeight);
                e.Graphics.FillRectangle(brush, inset, inset + barHeight * 2, Math.Max(1, Width - inset * 4), barHeight);
                e.Graphics.FillRectangle(brush, inset, inset + barHeight * 4, Math.Max(1, Width - inset * 3), barHeight);
                return;
            }

            int captionHeight = Math.Min(Theme.Scale(this, 22), Math.Max(16, Height / 3));
            var glyphBounds = new Rectangle(0, 0, Width, Math.Max(0, Height - captionHeight));
            using (var glyphFont = new Font("Segoe UI Symbol", Math.Max(16, Theme.Scale(this, 24)), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                TextRenderer.DrawText(e.Graphics, "▶", glyphFont, glyphBounds, Theme.P.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            using var captionFont = Theme.CreateFont(Theme.FontSmall);
            TextRenderer.DrawText(e.Graphics, "Thumbnail unavailable", captionFont,
                new Rectangle(2, Height - captionHeight, Math.Max(0, Width - 4), captionHeight),
                Theme.P.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    class ValidationInputFrame : Panel
    {
        bool invalid;

        internal void SetInvalid(bool value)
        {
            if (invalid == value) return;
            invalid = value;
            Invalidate();
        }

        public ValidationInputFrame()
        {
            DoubleBuffered = true;
            Padding = new Padding(2);
            BackColor = Theme.P.Input;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var bounds = ClientRectangle;
            bounds.Width--;
            bounds.Height--;
            using var pen = new Pen(invalid ? Theme.P.Error : Theme.P.Border);
            e.Graphics.DrawRectangle(pen, bounds);
        }
    }

    class SearchInputFrame : Panel
    {
        public SearchInputFrame()
        {
            DoubleBuffered = true;
            Padding = new Padding(2);
            BackColor = Theme.P.Input;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var bounds = ClientRectangle;
            bounds.Width--;
            bounds.Height--;
            using var pen = new Pen(Theme.P.Border);
            e.Graphics.DrawRectangle(pen, bounds);
        }
    }

    class ValidationMessageLabel : Label
    {
        public ValidationMessageLabel()
        {
            ForeColor = Theme.P.Error;
            AccessibleRole = AccessibleRole.Alert;
        }
    }

    // ====================== ফ্লিকার-ফ্রি ListView ======================
    class BufferedListView : ListView
    {
        const int WmEraseBkgnd = 0x0014;
        const int LvmGetHeader = 0x101F;
        const int LvmGetExtendedListViewStyle = 0x1037;
        const int LvmSetExtendedListViewStyle = 0x1036;
        const int LvsExGridLines = 0x00000001;
        const int LvsExDoubleBuffer = 0x00010000;
        ThemedListViewHeader? themedHeader;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public BufferedListView()
        {
            DoubleBuffered = true;
            GridLines = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            long current = SendMessage(Handle, LvmGetExtendedListViewStyle, IntPtr.Zero, IntPtr.Zero).ToInt64();
            long requested = (current | LvsExDoubleBuffer) & ~LvsExGridLines;
            SendMessage(Handle, LvmSetExtendedListViewStyle,
                new IntPtr(LvsExGridLines | LvsExDoubleBuffer), new IntPtr(requested));
            long verified = SendMessage(Handle, LvmGetExtendedListViewStyle, IntPtr.Zero, IntPtr.Zero).ToInt64();
            System.Diagnostics.Debug.Assert((verified & LvsExDoubleBuffer) != 0 && (verified & LvsExGridLines) == 0,
                "The download list must be double buffered with native grid lines disabled.");

            IntPtr header = SendMessage(Handle, LvmGetHeader, IntPtr.Zero, IntPtr.Zero);
            if (header != IntPtr.Zero)
            {
                themedHeader?.Dispose();
                themedHeader = new ThemedListViewHeader(header);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            themedHeader?.Dispose();
            themedHeader = null;
            base.OnHandleDestroyed(e);
        }

        internal void InvalidateHeader() => themedHeader?.Invalidate();

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmEraseBkgnd && m.WParam != IntPtr.Zero)
            {
                using var graphics = Graphics.FromHdc(m.WParam);
                using var brush = new SolidBrush(Theme.P.Window);
                graphics.FillRectangle(brush, ClientRectangle);
                m.Result = new IntPtr(1);
                return;
            }
            base.WndProc(ref m);
        }

        sealed class ThemedListViewHeader : NativeWindow, IDisposable
        {
            const int WmPaint = 0x000F;
            const int WmEraseBkgnd = 0x0014;
            const int HdmGetItemCount = 0x1200;
            const int HdmGetItemRect = 0x1207;

            [StructLayout(LayoutKind.Sequential)]
            struct NativeRect
            {
                public int Left, Top, Right, Bottom;
            }

            [DllImport("user32.dll", SetLastError = true)]
            static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

            [DllImport("user32.dll")]
            static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll")]
            static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);

            public ThemedListViewHeader(IntPtr handle) => AssignHandle(handle);

            public void Invalidate()
            {
                if (Handle != IntPtr.Zero) InvalidateRect(Handle, IntPtr.Zero, true);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmEraseBkgnd && m.WParam != IntPtr.Zero)
                {
                    FillBackground(m.WParam);
                    m.Result = new IntPtr(1);
                    return;
                }

                if (m.Msg == WmPaint)
                {
                    base.WndProc(ref m);
                    PaintFiller();
                    return;
                }

                base.WndProc(ref m);
            }

            void FillBackground(IntPtr hdc)
            {
                if (!GetClientRect(Handle, out var client)) return;
                using var graphics = Graphics.FromHdc(hdc);
                using var brush = new SolidBrush(Theme.P.Header);
                graphics.FillRectangle(brush, client.Left, client.Top,
                    Math.Max(0, client.Right - client.Left), Math.Max(0, client.Bottom - client.Top));
            }

            void PaintFiller()
            {
                if (Handle == IntPtr.Zero || !GetClientRect(Handle, out var client)) return;
                int left = client.Left;
                int count = (int)SendMessage(Handle, HdmGetItemCount, IntPtr.Zero, IntPtr.Zero).ToInt64();
                if (count > 0)
                {
                    IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
                    try
                    {
                        if (SendMessage(Handle, HdmGetItemRect, new IntPtr(count - 1), memory) != IntPtr.Zero)
                            left = Marshal.PtrToStructure<NativeRect>(memory).Right;
                    }
                    finally { Marshal.FreeHGlobal(memory); }
                }

                if (left >= client.Right) return;
                IntPtr hdc = GetDC(Handle);
                if (hdc == IntPtr.Zero) return;
                try
                {
                    using var graphics = Graphics.FromHdc(hdc);
                    using var brush = new SolidBrush(Theme.P.Header);
                    using var border = new Pen(Theme.P.Border);
                    graphics.FillRectangle(brush, left, client.Top, client.Right - left, client.Bottom - client.Top);
                    graphics.DrawLine(border, left, client.Bottom - 1, client.Right, client.Bottom - 1);
                }
                finally { ReleaseDC(Handle, hdc); }
            }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero) ReleaseHandle();
            }
        }
    }

    class BufferedListBox : ListBox
    {
        const int WmEraseBkgnd = 0x0014;

        public BufferedListBox()
        {
            DoubleBuffered = true;

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw,
                true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmEraseBkgnd && m.WParam != IntPtr.Zero)
            {
                using var graphics = Graphics.FromHdc(m.WParam);
                using var brush = new SolidBrush(BackColor);
                graphics.FillRectangle(brush, ClientRectangle);
                m.Result = new IntPtr(1);
                return;
            }
            base.WndProc(ref m);
        }
    }
}
