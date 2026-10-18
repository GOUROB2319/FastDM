using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace FastDM
{
    // নির্বাচিত ডাউনলোডের বিস্তারিত: তথ্য, স্পিড গ্রাফ (শেষ ৬০ সেকেন্ড) আর প্রতি কানেকশনের প্রগ্রেস
    class DetailsPanel : Control
    {
        public const int HistoryLength = 120;     // ০.৫ সেকেন্ড x ১২০ = ৬০ সেকেন্ড

        DownloadItem? item;
        readonly Font bold;
        readonly Font underline;
        readonly ToolTip tips;

        // "Save to" and "Link" values are clickable. Their areas are set while painting.
        Rectangle saveRect, linkRect;
        int hover;                                   // 0 = nothing, 1 = "Save to", 2 = "Link"
        int detailScrollOffset;

        public event Action<DownloadItem>? OpenFolderRequested;
        public event Action<DownloadItem>? OpenLinkRequested;

        public DownloadItem? Item => item;

        public DetailsPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Bottom;
            Height = Theme.Scale(this, 212);
            Font = Theme.CreateFont(Theme.FontBody);
            bold = Theme.CreateFont(Theme.FontHeading, FontStyle.Bold);
            underline = new Font(Font, FontStyle.Underline);
            tips = new ToolTip { ShowAlways = true, InitialDelay = 400 };
        }

        public void SetItem(DownloadItem? it)
        {
            item = it;
            detailScrollOffset = 0;
            tips.SetToolTip(this, it == null ? null : "Scroll for more details. Right-click Save to or Link to copy its value.");
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                bold.Dispose();
                underline.Dispose();
                tips.Dispose();
            }
            base.Dispose(disposing);
        }

        static bool IsWebLink(string text) =>
            Uri.TryCreate(text, UriKind.Absolute, out var u) &&
            (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

        int HitTest(Point p) =>
            item == null ? 0 : saveRect.Contains(p) ? 1 : linkRect.Contains(p) ? 2 : 0;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int h = HitTest(e.Location);
            tips.SetToolTip(this, h == 1 ? "Open folder. Right-click to copy path." :
                h == 2 ? "Open link. Right-click to copy URL." :
                item == null ? null : "Scroll for more details.");
            if (h == hover) return;

            hover = h;
            Cursor = h != 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (hover == 0) return;

            hover = 0;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            var it = item;
            if (it == null) return;

            int h = HitTest(e.Location);
            if (e.Button == MouseButtons.Right && h != 0)
            {
                try { Clipboard.SetText(h == 1 ? it.Folder ?? "" : it.Url ?? ""); }
                catch (Exception exIgnored) { AppLog.Write("DetailsPanel clipboard (ignored): " + exIgnored.Message); }
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (h == 1) OpenFolderRequested?.Invoke(it);
                else if (h == 2) OpenLinkRequested?.Invoke(it);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (item == null) return;
            detailScrollOffset = Math.Max(0, detailScrollOffset + (e.Delta < 0 ? 1 : -1));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.Clear(p.Window);
            using (var pen = new Pen(p.Border)) g.DrawLine(pen, 0, 0, Width, 0);

            var snapshot = item;
            if (snapshot == null)
            {
                TextRenderer.DrawText(g, "Select a download to see its details", Font, ClientRectangle,
                    p.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = Theme.Scale(this, 16);
            int top = Theme.Scale(this, 10);
            int leftW = Math.Max(Theme.Scale(this, 280), (int)(Width * 0.42));

            DrawInfo(g, p, snapshot, new Rectangle(pad, top, leftW - pad, Height - Theme.Scale(this, 16)));

            var right = new Rectangle(leftW + Theme.Scale(this, 12), top,
                Width - leftW - Theme.Scale(this, 12) - pad, Height - Theme.Scale(this, 16));
            if (right.Width > Theme.Scale(this, 160))
            {
                DrawGraph(g, p, snapshot, right);
                DrawConnections(g, p, snapshot, new Rectangle(right.X, right.Y + Theme.Scale(this, 124), right.Width, Theme.Scale(this, 70)));
            }
        }

        // ---------- বাম পাশ: তথ্য ----------
        void DrawInfo(Graphics g, Palette p, DownloadItem it, Rectangle r)
        {
            const TextFormatFlags f = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.Left;

            int S(int n) => Theme.Scale(this, n);
            TextRenderer.DrawText(g, MainForm.DisplayFileName(it.FileName), bold, new Rectangle(r.X, r.Y, r.Width, S(26)), p.Text, f);

            string size = it.TotalBytes > 0
                ? Fmt(it.Downloaded) + " / " + Fmt(it.TotalBytes) +
                  (it.YtDlp != null && YtDlpRetryPolicy.IsProcessing(it.ProgressStage) ? "" : "  (" + it.Percent.ToString("0.0") + "%)")
                : Fmt(it.Downloaded) + " / unknown";

            string etaText = Eta(it);
            string status = it.DeletedAt.HasValue
                ? "Deleted: " + (it.DeletedReason ?? "")
                : it.State == DlState.Completed
                    ? "Complete"
                    : it.YtDlp != null && it.State == DlState.Downloading && it.ProgressStage != YtDlpStage.None
                        ? YtDlpRetryPolicy.StageLabel(it.ProgressStage)
                        : it.State.ToString();
            var rows = new System.Collections.Generic.List<string[]>
            {
                new[] { "Status", status },
                new[] { "Size", size },
                new[] { "Speed", it.State == DlState.Downloading && (it.YtDlp == null || YtDlpRetryPolicy.IsTransferring(it.ProgressStage)) ? Fmt(it.Speed) + "/s" : "—" },
                new[] { "ETA", etaText.Length > 0 ? etaText : "—" },
                new[] { "Resume", it.Stream != null || it.YtDlp != null || it.SupportsRange || !DownloadEngine.IsHttp(it.Url ?? "http://") ? "Supported" : "Not supported" },
                new[] { "Save to", it.Folder ?? "" },
                new[] { "Link", it.Url ?? "" },
                new[] { "Added", it.Added.ToString("dd MMM yyyy HH:mm") }
            };
            if (it.State == DlState.Error && !string.IsNullOrEmpty(it.Error))
            {
                string error = AppLog.Redact(it.Error);
                if (it.YtDlp != null)
                {
                    var kind = YtDlpDiagnostics.Classify(error);
                    error = YtDlpDiagnostics.Describe(kind, UpdateChecker.IsPackaged).Summary + "  " + error;
                }
                rows.Insert(1, new[] { "Error", error });
            }
            else if (!string.IsNullOrEmpty(it.Error))
                rows.Insert(1, new[] { "Note", it.Error });

            int rowTop = r.Y + S(32);
            int lh = S(21);
            int visibleRows = Math.Max(1, (r.Bottom - rowTop) / lh);
            detailScrollOffset = Math.Clamp(detailScrollOffset, 0, Math.Max(0, rows.Count - visibleRows));
            int y = rowTop - detailScrollOffset * lh;

            saveRect = Rectangle.Empty;
            linkRect = Rectangle.Empty;

            foreach (var row in rows)
            {
                if (y + lh <= rowTop) { y += lh; continue; }
                if (y >= r.Bottom) break;
                TextRenderer.DrawText(g, row[0], Font, new Rectangle(r.X, y, S(68), lh), p.SubText, f);

                var valueRect = new Rectangle(r.X + S(72), y, r.Width - S(72), lh);
                bool isSave = row[0] == "Save to" && row[1].Length > 0;
                bool isLink = row[0] == "Link" && IsWebLink(row[1]);

                Color vc = row[0] == "Error" ? p.Error : p.Text;
                Font vf = Font;

                if (isSave || isLink)
                {
                    vc = p.Accent;

                    int textW = TextRenderer.MeasureText(g, row[1], Font, new Size(valueRect.Width, lh), f).Width;
                    var hit = new Rectangle(valueRect.X, valueRect.Y, Math.Min(valueRect.Width, textW), lh);

                    if (isSave) saveRect = hit; else linkRect = hit;
                    if (hover == (isSave ? 1 : 2)) vf = underline;
                }

                TextRenderer.DrawText(g, row[1], vf, valueRect, vc, f);
                y += lh;
            }
        }

        // ---------- ডান পাশ: স্পিড গ্রাফ ----------
        void DrawGraph(Graphics g, Palette p, DownloadItem it, Rectangle r)
        {
            const TextFormatFlags f = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                                      TextFormatFlags.VerticalCenter;

            int S(int n) => Theme.Scale(this, n);
            TextRenderer.DrawText(g, "Speed (last 60 s)", Font, new Rectangle(r.X, r.Y, r.Width / 2, S(20)),
                p.SubText, f | TextFormatFlags.Left);
            string now = it.State == DlState.Downloading && (it.YtDlp == null || YtDlpRetryPolicy.IsTransferring(it.ProgressStage)) ? Fmt(it.Speed) + "/s" : "—";
            TextRenderer.DrawText(g, now, bold, new Rectangle(r.X + r.Width / 2, r.Y - S(2), r.Width / 2, S(24)),
                p.Text, f | TextFormatFlags.Right);

            var gr = new Rectangle(r.X, r.Y + S(24), r.Width, S(92));
            using (var b = new SolidBrush(p.RowAlt)) g.FillRectangle(b, gr);
            if (it.YtDlp != null && YtDlpRetryPolicy.IsProcessing(it.ProgressStage))
            {
                using (var pen = new Pen(p.Border)) g.DrawRectangle(pen, gr);
                TextRenderer.DrawText(g, YtDlpRetryPolicy.StageLabel(it.ProgressStage) + "…",
                    bold, gr, p.Text, f | TextFormatFlags.HorizontalCenter);
                return;
            }
            using (var pen = new Pen(p.Border))
            {
                g.DrawRectangle(pen, gr);
                for (int i = 1; i <= 2; i++)
                {
                    int gy = gr.Y + (gr.Height - S(18)) * i / 3;
                    g.DrawLine(pen, gr.X, gy, gr.Right, gy);
                }
            }

            double[] h = it.SpeedHistory.ToArray();
            if (h.Length < 2)
            {
                DrawGraphAxisLabels(g, p, gr, 1024 * 1.15);
                return;
            }

            double max = Math.Max(h.Max(), 1024) * 1.15;
            int plotBottom = gr.Bottom - S(18);
            int plotHeight = gr.Height - S(22);
            float step = (gr.Width - 2) / (float)(HistoryLength - 1);
            var pts = new PointF[h.Length];
            for (int i = 0; i < h.Length; i++)
            {
                float x = gr.Right - 1 - (h.Length - 1 - i) * step;
                float y = plotBottom - S(3) - (float)(h[i] / max * Math.Max(1, plotHeight - S(8)));
                pts[i] = new PointF(x, y);
            }

            var poly = new PointF[pts.Length + 2];
            Array.Copy(pts, poly, pts.Length);
            poly[pts.Length] = new PointF(pts[pts.Length - 1].X, plotBottom - 1);
            poly[pts.Length + 1] = new PointF(pts[0].X, plotBottom - 1);

            using (var fill = new SolidBrush(Color.FromArgb(55, p.Accent))) g.FillPolygon(fill, poly);
            using (var line = new Pen(p.Accent, 2f) { LineJoin = LineJoin.Round }) g.DrawLines(line, pts);

            DrawGraphAxisLabels(g, p, gr, max);
        }

        void DrawGraphAxisLabels(Graphics g, Palette p, Rectangle gr, double max)
        {
            int S(int n) => Theme.Scale(this, n);
            const TextFormatFlags f = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                                      TextFormatFlags.VerticalCenter;
            int plotBottom = gr.Bottom - S(18);
            var maxLabel = new Rectangle(gr.X + S(6), gr.Y + S(2), S(120), S(18));
            using (var bg = new SolidBrush(p.RowAlt)) g.FillRectangle(bg, maxLabel);
            TextRenderer.DrawText(g, Fmt(max) + "/s", Font, maxLabel, p.SubText,
                f | TextFormatFlags.Left);
            TextRenderer.DrawText(g, "60 s ago", Font,
                new Rectangle(gr.X + S(4), plotBottom, S(90), S(16)), p.SubText,
                f | TextFormatFlags.Left);
            TextRenderer.DrawText(g, "now", Font,
                new Rectangle(gr.Right - S(54), plotBottom, S(50), S(16)), p.SubText,
                f | TextFormatFlags.Right);
        }

        // ---------- ডান পাশ নিচে: কানেকশন/সেগমেন্ট ম্যাপ ----------
        void DrawConnections(Graphics g, Palette p, DownloadItem it, Rectangle r)
        {
            int S(int n) => Theme.Scale(this, n);
            const TextFormatFlags f = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.Left;

            if (it.YtDlp != null)
            {
                string ycap = "Downloaded by yt-dlp" + (string.IsNullOrEmpty(it.StatusNote) ? "" : "  •  " + it.StatusNote);
                TextRenderer.DrawText(g, ycap, Font, new Rectangle(r.X, r.Y, r.Width, S(20)), p.SubText, f);

                var ybar = new Rectangle(r.X, r.Y + S(26), r.Width, S(18));
                using (var b = new SolidBrush(p.Track)) g.FillRectangle(b, ybar);
                int yw = YtDlpRetryPolicy.IsProcessing(it.ProgressStage) ? 0 : (int)(ybar.Width * it.Percent / 100.0);
                if (yw > 0)
                    using (var b = new SolidBrush(it.State == DlState.Completed ? p.Success : p.Accent))
                        g.FillRectangle(b, ybar.X, ybar.Y, yw, ybar.Height);
                using (var pen = new Pen(p.Border)) g.DrawRectangle(pen, ybar);
                return;
            }

            if (it.Stream != null)
            {
                string cap = "Stream (" + (it.Stream.Kind ?? "").ToUpperInvariant() + "): " + it.StreamDone + " / " + it.StreamTotal + " segments" +
                             (string.IsNullOrEmpty(it.StatusNote) ? "" : "  •  " + it.StatusNote);
                TextRenderer.DrawText(g, cap, Font, new Rectangle(r.X, r.Y, r.Width, S(20)), p.SubText, f);

                var sbar = new Rectangle(r.X, r.Y + S(26), r.Width, S(18));
                using (var b = new SolidBrush(p.Track)) g.FillRectangle(b, sbar);
                int sw = (int)(sbar.Width * it.Percent / 100.0);
                if (sw > 0)
                    using (var b = new SolidBrush(it.State == DlState.Completed ? p.Success : p.Accent))
                        g.FillRectangle(b, sbar.X, sbar.Y, sw, sbar.Height);
                using (var pen = new Pen(p.Border)) g.DrawRectangle(pen, sbar);
                return;
            }

            var segs = it.Segments.ToArray();
            bool segmented = it.TotalBytes > 0 && segs.Length > 1 && segs.All(s => s.End >= s.Start);

            string caption;
            if (segmented)
            {
                int done = segs.Count(s => s.Finished);
                caption = "Connections: " + segs.Length + "  •  finished: " + done;
            }
            else caption = "Connections: 1 (single stream)";
            TextRenderer.DrawText(g, caption, Font, new Rectangle(r.X, r.Y, r.Width, 20), p.SubText, f);

            var bar = new Rectangle(r.X, r.Y + 26, r.Width, 18);
            using (var b = new SolidBrush(p.Track)) g.FillRectangle(b, bar);

            if (segmented)
            {
                double total = it.TotalBytes;
                foreach (var s in segs)
                {
                    int x1 = bar.X + (int)(bar.Width * (s.Start / total));
                    int x2 = bar.X + (int)(bar.Width * ((s.End + 1) / total));
                    int w = Math.Max(1, x2 - x1);
                    long len = s.End - s.Start + 1;
                    int fw = s.Finished ? w : (int)(w * Math.Min(1.0, s.Downloaded / (double)Math.Max(1, len)));
                    if (fw > 0)
                        using (var b = new SolidBrush(s.Finished ? p.Success : p.Accent))
                            g.FillRectangle(b, x1, bar.Y, fw, bar.Height);
                    using (var pen = new Pen(p.Window)) g.DrawLine(pen, x1, bar.Y, x1, bar.Bottom);
                }
            }
            else if (it.TotalBytes > 0)
            {
                int fw = (int)(bar.Width * it.Percent / 100.0);
                if (fw > 0)
                    using (var b = new SolidBrush(it.State == DlState.Completed ? p.Success : p.Accent))
                        g.FillRectangle(b, bar.X, bar.Y, fw, bar.Height);
            }
            using (var pen = new Pen(p.Border)) g.DrawRectangle(pen, bar);
        }

        // ---------- ছোট হেল্পার ----------
        static string Fmt(double b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return (i == 0 ? b.ToString("0") : b.ToString("0.##")) + " " + u[i];
        }

        static string Eta(DownloadItem it)
        {
            if (it.YtDlp != null && !YtDlpRetryPolicy.IsTransferring(it.ProgressStage)) return "";
            if (it.State != DlState.Downloading || it.Speed < 1 || it.TotalBytes <= 0) return "";
            var t = TimeSpan.FromSeconds((it.TotalBytes - it.Downloaded) / it.Speed);
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            if (t.TotalMinutes >= 1) return t.Minutes + "m " + t.Seconds + "s";
            return t.Seconds + "s";
        }
    }
}
