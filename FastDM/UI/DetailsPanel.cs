using System;
using System.Collections.Generic;
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
        Rectangle errorRect;
        Rectangle scrollTrackRect, scrollThumbRect;
        int hover;                                   // 0 = nothing, 1 = "Save to", 2 = "Link"
        int detailScrollOffset;
        int maxDetailScroll;
        int visibleDetailRows;
        int actionFocus;
        bool draggingScroll;
        int scrollDragGrip;

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
            TabStop = true;
            bold = Theme.CreateFont(Theme.FontHeading, FontStyle.Bold);
            underline = new Font(Font, FontStyle.Underline);
            tips = new ToolTip { ShowAlways = true, InitialDelay = 400 };
            AccessibleName = "Download details";
            AccessibleDescription = "Details for the selected download. Use the scrollbar, mouse wheel, or Up and Down to scroll; use Left and Right to select an action, then Enter or Space to open it.";
            AccessibleRole = AccessibleRole.Pane;
        }

        public void SetItem(DownloadItem? it)
        {
            item = it;
            detailScrollOffset = 0;
            maxDetailScroll = 0;
            visibleDetailRows = 0;
            saveRect = linkRect = errorRect = Rectangle.Empty;
            scrollTrackRect = scrollThumbRect = Rectangle.Empty;
            actionFocus = it != null && !string.IsNullOrEmpty(it.Folder) ? 1 : IsWebLink(it?.Url ?? "") ? 2 : 0;
            AccessibleName = it == null ? "Download details" : "Download details for " + MainForm.DisplayFileName(it.FileName);
            AccessibleDescription = it == null
                ? "Select a download to see its details."
                : string.Join(". ", BuildRows(it).ConvertAll(row => row[0] + ": " + row[1])) +
                  ". Use the scrollbar, mouse wheel, or Up and Down to scroll. Use Left and Right to select Save to or Link, then Enter or Space to activate. Right-click either value to copy it.";
            tips.SetToolTip(this, it == null ? null : "Use the mouse wheel or Up/Down to scroll. Tab to the details, then Left/Right and Enter to open an action. Right-click Save to or Link to copy its value.");
            Invalidate();
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new DetailsAccessibleObject(this);

        int ActionCount => (item != null && !string.IsNullOrEmpty(item.Folder) ? 1 : 0) + (IsWebLink(item?.Url ?? "") ? 1 : 0);

        int ActionAt(int index)
        {
            if (index < 0 || index >= ActionCount) return 0;
            bool hasFolder = item != null && !string.IsNullOrEmpty(item.Folder);
            return hasFolder && index == 0 ? 1 : 2;
        }

        Rectangle ActionBounds(int action) => action == 1 ? saveRect : action == 2 ? linkRect : Rectangle.Empty;

        string ActionName(int action) => action == 1
            ? "Save to: " + (item?.Folder ?? "")
            : "Link: " + (item?.Url ?? "");

        string ActionDescription(int action) => action == 1
            ? "Open this folder. Right-click to copy its full path."
            : "Open this link. Right-click to copy its full URL.";

        void ActivateAction(int action)
        {
            var snapshot = item;
            if (snapshot == null) return;
            if (action == 1 && !string.IsNullOrEmpty(snapshot.Folder)) OpenFolderRequested?.Invoke(snapshot);
            else if (action == 2 && IsWebLink(snapshot.Url ?? "")) OpenLinkRequested?.Invoke(snapshot);
        }

        void EnsureActionVisible(int action)
        {
            if (item == null || visibleDetailRows <= 0) return;
            var rows = BuildRows(item);
            int index = rows.FindIndex(row => row[0] == (action == 1 ? "Save to" : "Link"));
            if (index < 0) return;
            if (index < detailScrollOffset) detailScrollOffset = index;
            else if (index >= detailScrollOffset + visibleDetailRows)
                detailScrollOffset = index - visibleDetailRows + 1;
            Invalidate();
        }

        sealed class DetailsAccessibleObject : Control.ControlAccessibleObject
        {
            readonly DetailsPanel owner;

            public DetailsAccessibleObject(DetailsPanel owner) : base(owner) => this.owner = owner;

            public override int GetChildCount() => owner.ActionCount;

            public override AccessibleObject? GetChild(int index) =>
                index >= 0 && index < owner.ActionCount
                    ? new DetailsActionAccessibleObject(owner, owner.ActionAt(index))
                    : null;
        }

        sealed class DetailsActionAccessibleObject : AccessibleObject
        {
            readonly DetailsPanel owner;
            readonly int action;

            public DetailsActionAccessibleObject(DetailsPanel owner, int action)
            {
                this.owner = owner;
                this.action = action;
            }

            public override AccessibleObject? Parent => owner.AccessibilityObject;
            public override string? Name { get => owner.ActionName(action); set { } }
            public override string? Description => owner.ActionDescription(action);
            public override string? DefaultAction => action == 1 ? "Open folder" : "Open link";
            public override AccessibleRole Role => AccessibleRole.Link;
            public override AccessibleStates State => AccessibleStates.Focusable | AccessibleStates.Selectable |
                (owner.Focused && owner.actionFocus == action ? AccessibleStates.Focused : AccessibleStates.None);
            public override Rectangle Bounds
            {
                get
                {
                    var bounds = owner.ActionBounds(action);
                    return bounds.IsEmpty ? Rectangle.Empty : owner.RectangleToScreen(bounds);
                }
            }

            public override void DoDefaultAction()
            {
                owner.actionFocus = action;
                owner.EnsureActionVisible(action);
                owner.ActivateAction(action);
            }

            public override void Select(AccessibleSelection flags)
            {
                if ((flags & AccessibleSelection.TakeFocus) == 0) return;
                owner.actionFocus = action;
                owner.EnsureActionVisible(action);
                owner.Focus();
                owner.Invalidate();
            }
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

            if (draggingScroll)
            {
                SetScrollFromThumb(e.Y - scrollDragGrip);
                return;
            }

            int h = HitTest(e.Location);
            tips.SetToolTip(this, h == 1 ? "Save to: " + (item?.Folder ?? "") + " — click to open, right-click to copy." :
                h == 2 ? "Link: " + (item?.Url ?? "") + " — click to open, right-click to copy." :
                errorRect.Contains(e.Location) ? "Full error: " + AppLog.Redact(item?.Error ?? "") :
                item == null ? null : "Use the mouse wheel or Up/Down to scroll for more details.");
            var cursor = h != 0 ? Cursors.Hand : scrollTrackRect.Contains(e.Location) ? Cursors.SizeNS : Cursors.Default;
            if (h == hover && Cursor == cursor) return;

            hover = h;
            Cursor = cursor;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || maxDetailScroll == 0 || !scrollTrackRect.Contains(e.Location)) return;
            Focus();
            if (scrollThumbRect.Contains(e.Location))
            {
                draggingScroll = true;
                scrollDragGrip = e.Y - scrollThumbRect.Y;
                Capture = true;
            }
            else
            {
                SetScrollFromThumb(e.Y - scrollThumbRect.Height / 2);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !draggingScroll) return;
            draggingScroll = false;
            Capture = false;
            Cursor = Cursors.Default;
        }

        void SetScrollFromThumb(int thumbTop)
        {
            int travel = Math.Max(0, scrollTrackRect.Height - scrollThumbRect.Height);
            int relative = Math.Clamp(thumbTop - scrollTrackRect.Y, 0, travel);
            detailScrollOffset = travel == 0 ? 0 : (int)Math.Round(relative * maxDetailScroll / (double)travel);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (hover == 0)
            {
                Cursor = Cursors.Default;
                return;
            }

            hover = 0;
            Cursor = scrollTrackRect.Contains(PointToClient(Cursor.Position)) ? Cursors.SizeNS : Cursors.Default;
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
                Focus();
                if (h != 0)
                {
                    actionFocus = h;
                    EnsureActionVisible(h);
                    ActivateAction(h);
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (item == null) return;

            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                ScrollDetails(e.KeyCode == Keys.Down ? 1 : -1);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                int count = ActionCount;
                if (count > 0)
                {
                    var actions = new List<int>();
                    for (int i = 0; i < count; i++) actions.Add(ActionAt(i));
                    int current = actions.IndexOf(actionFocus);
                    actionFocus = actions[(current + (e.KeyCode == Keys.Right ? 1 : count - 1)) % count];
                    EnsureActionVisible(actionFocus);
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && actionFocus != 0)
            {
                ActivateAction(actionFocus);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (actionFocus == 0 && ActionCount > 0) actionFocus = ActionAt(0);
            EnsureActionVisible(actionFocus);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (item == null) return;
            int steps = Math.Max(1, Math.Abs(e.Delta) / 120);
            ScrollDetails(e.Delta < 0 ? steps : -steps);
        }

        void ScrollDetails(int amount)
        {
            detailScrollOffset = Math.Clamp(detailScrollOffset + amount, 0, maxDetailScroll);
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

            var info = new Rectangle(pad, top, leftW - pad, Height - Theme.Scale(this, 16));
            var state = g.Save();
            g.SetClip(info);
            DrawInfo(g, p, snapshot, info);
            g.Restore(state);

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

            var rows = BuildRows(it);
            int rowTop = r.Y + S(32);
            int lh = S(21);
            int visibleRows = Math.Max(1, (r.Bottom - rowTop) / lh);
            visibleDetailRows = visibleRows;
            maxDetailScroll = Math.Max(0, rows.Count - visibleRows);
            detailScrollOffset = Math.Clamp(detailScrollOffset, 0, maxDetailScroll);
            int y = rowTop - detailScrollOffset * lh;
            int contentWidth = Math.Max(0, r.Width - S(12));

            saveRect = Rectangle.Empty;
            linkRect = Rectangle.Empty;
            errorRect = Rectangle.Empty;
            scrollTrackRect = Rectangle.Empty;
            scrollThumbRect = Rectangle.Empty;
            Rectangle focusRect = Rectangle.Empty;

            foreach (var row in rows)
            {
                if (y + lh <= rowTop) { y += lh; continue; }
                if (y >= r.Bottom) break;
                TextRenderer.DrawText(g, row[0], Font, new Rectangle(r.X, y, S(68), lh), p.SubText, f);

                var valueRect = new Rectangle(r.X + S(72), y, Math.Max(0, contentWidth - S(72)), lh);
                bool isSave = row[0] == "Save to" && row[1].Length > 0;
                bool isLink = row[0] == "Link" && IsWebLink(row[1]);

                Color vc = row[0] == "Error" ? p.Error : p.Text;
                Font vf = Font;

                if (isSave || isLink)
                {
                    vc = p.AccentText;
                    int textW = TextRenderer.MeasureText(g, row[1], Font, new Size(valueRect.Width, lh), f).Width;
                    var hit = new Rectangle(valueRect.X, valueRect.Y, Math.Min(valueRect.Width, textW), lh);
                    if (isSave) saveRect = hit; else linkRect = hit;
                    if (hover == (isSave ? 1 : 2)) vf = underline;
                    if (Focused && actionFocus == (isSave ? 1 : 2) && !hit.IsEmpty)
                        focusRect = hit;
                }
                else if (row[0] == "Error" || row[0] == "Note")
                {
                    int textW = TextRenderer.MeasureText(g, row[1], Font, new Size(valueRect.Width, lh), f).Width;
                    errorRect = new Rectangle(valueRect.X, valueRect.Y, Math.Min(valueRect.Width, textW), lh);
                }

                TextRenderer.DrawText(g, row[1], vf, valueRect, vc, f);
                y += lh;
            }

            if (!focusRect.IsEmpty)
                ControlPaint.DrawFocusRectangle(g, focusRect, p.Text, p.Window);

            if (maxDetailScroll > 0)
            {
                scrollTrackRect = new Rectangle(r.Right - S(8), rowTop, S(8), Math.Max(1, r.Bottom - rowTop));
                using (var brush = new SolidBrush(p.Track)) g.FillRectangle(brush, scrollTrackRect);
                int thumbHeight = Math.Clamp((int)Math.Round(scrollTrackRect.Height * visibleRows / (double)rows.Count), Math.Min(S(18), scrollTrackRect.Height), scrollTrackRect.Height);
                int travel = Math.Max(0, scrollTrackRect.Height - thumbHeight);
                int thumbY = scrollTrackRect.Y + (maxDetailScroll == 0 ? 0 : travel * detailScrollOffset / maxDetailScroll);
                scrollThumbRect = new Rectangle(scrollTrackRect.X, thumbY, scrollTrackRect.Width, thumbHeight);
                using (var brush = new SolidBrush(p.Accent)) g.FillRectangle(brush, scrollThumbRect);
            }
        }

        List<string[]> BuildRows(DownloadItem it)
        {
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
            return rows;
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
