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
        public ThumbnailSkeleton()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            AccessibleName = "Thumbnail loading placeholder";
            AccessibleDescription = "The video thumbnail is loading.";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Theme.P.Hover);
            int inset = Theme.Scale(this, 12);
            int barHeight = Math.Max(3, Theme.Scale(this, 5));
            using var brush = new SolidBrush(Theme.P.Border);
            e.Graphics.FillRectangle(brush, inset, inset, Math.Max(1, Width - inset * 2), barHeight);
            e.Graphics.FillRectangle(brush, inset, inset + barHeight * 2, Math.Max(1, Width - inset * 4), barHeight);
            e.Graphics.FillRectangle(brush, inset, inset + barHeight * 4, Math.Max(1, Width - inset * 3), barHeight);
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
            using var pen = new Pen(invalid ? Theme.P.Error : Theme.P.InputBorder);
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
        public BufferedListView()
        {
            DoubleBuffered = true;
        }
    }

    class BufferedListBox : ListBox
    {
        public BufferedListBox()
        {
            DoubleBuffered = true;

            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw,
                true);
        }
    }
}
