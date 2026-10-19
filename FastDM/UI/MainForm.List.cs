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
    // MainForm part: download list rows, colors, history.
    public partial class MainForm
    {
        static Color RowColor(DownloadItem it) =>
            it.DeletedAt.HasValue ? Theme.P.SubText : StateColor(it.State);

        static Color StateColor(DlState s)
        {
            switch (s)
            {
                case DlState.Downloading:
                    return Theme.P.Accent;

                case DlState.Completed:
                    return Theme.P.Success;

                case DlState.Paused:
                    return Theme.P.Warning;

                case DlState.Error:
                    return Theme.P.Error;

                default:
                    return Theme.P.SubText;
            }
        }

        // ---------- লিস্ট ----------
        static bool Match(
            int f,
            DownloadItem i)
        {
            switch (f)
            {
                case 1:
                    return i.State == DlState.Downloading;

                case 2:
                    return i.State == DlState.Queued ||
                           i.State == DlState.Paused;

                case 3:
                    return i.State == DlState.Completed;

                case 4:
                    return i.State == DlState.Error;

                default:
                    return true;
            }
        }

        IEnumerable<DownloadItem> VisibleItems()
        {
            IEnumerable<DownloadItem> source = filter == DeletedFilter
                ? deleted
                : items.Where(i => Match(filter, i));

            string query = searchBox?.Text.Trim() ?? "";
            if (query.Length > 0)
                source = source.Where(i =>
                    DisplayFileName(i.FileName).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    (i.Url ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    (i.Error ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    i.State.ToString().Contains(query, StringComparison.CurrentCultureIgnoreCase));

            DateTime AddedOrDeleted(DownloadItem it) => it.DeletedAt ?? it.Added;
            switch (sortColumn)
            {
                case 0:
                    return sortAscending
                        ? source.OrderBy(i => DisplayFileName(i.FileName), StringComparer.CurrentCultureIgnoreCase).ThenBy(AddedOrDeleted)
                        : source.OrderByDescending(i => DisplayFileName(i.FileName), StringComparer.CurrentCultureIgnoreCase).ThenByDescending(AddedOrDeleted);
                case 1:
                    var sized = source.OrderBy(i => i.TotalBytes > 0 ? 0 : 1);
                    return sortAscending
                        ? sized.ThenBy(i => i.TotalBytes).ThenBy(i => DisplayFileName(i.FileName), StringComparer.CurrentCultureIgnoreCase)
                        : sized.ThenByDescending(i => i.TotalBytes).ThenBy(i => DisplayFileName(i.FileName), StringComparer.CurrentCultureIgnoreCase);
                case 2:
                    return sortAscending
                        ? source.OrderBy(i => i.Percent).ThenBy(AddedOrDeleted)
                        : source.OrderByDescending(i => i.Percent).ThenByDescending(AddedOrDeleted);
                default:
                    return sortAscending
                        ? source.OrderBy(AddedOrDeleted)
                        : source.OrderByDescending(AddedOrDeleted);
            }
        }

        void OnColumnClick(object? sender, ColumnClickEventArgs e)
        {
            if (e.Column != 0 && e.Column != 1 && e.Column != 2 && e.Column != 6) return;
            if (sortColumn == e.Column) sortAscending = !sortAscending;
            else
            {
                sortColumn = e.Column;
                sortAscending = e.Column == 0 || e.Column == 1 || e.Column == 2;
            }
            RebuildList();
            lv.Invalidate();
        }

        internal static string DisplayFileName(string? value)
            => DownloadDisplayName.Format(value);

        static string FormatListDate(DateTime value) =>
            value.Date == DateTime.Today
                ? "Today " + value.ToString("HH:mm")
                : value.ToString("dd MMM yyyy");

        static string FormatFullListDate(DateTime value) => value.ToString("dddd, dd MMMM yyyy HH:mm:ss");

        static string AddDateToTooltip(string? content, string label, DateTime value)
        {
            string date = label + ": " + FormatFullListDate(value);
            return string.IsNullOrWhiteSpace(content) ? date : content + Environment.NewLine + date;
        }

        void UpdateEmptyState()
        {
            if (lv.Items.Count > 0)
            {
                emptyState.Visible = false;
                return;
            }

            string query = searchBox?.Text.Trim() ?? "";
            if (query.Length > 0)
            {
                if (query.Length > 64) query = query.Substring(0, 61) + "...";
                emptyTitle.Text = "No matching downloads";
                emptyDescription.Text = "No results for \"" + query + "\". Clear the search or choose another filter.";
                emptyAdd.Visible = true;
            }
            else
            {
                switch (filter)
                {
                    case 0:
                        emptyTitle.Text = "No downloads yet";
                        emptyDescription.Text = "Paste a link or drag one here";
                        emptyAdd.Visible = true;
                        break;
                    case 1:
                        emptyTitle.Text = "Nothing downloading";
                        emptyDescription.Text = "Active downloads will appear here.";
                        emptyAdd.Visible = true;
                        break;
                    case 2:
                        emptyTitle.Text = "Nothing queued or paused";
                        emptyDescription.Text = "Queued and paused downloads will appear here.";
                        emptyAdd.Visible = true;
                        break;
                    case 3:
                        emptyTitle.Text = "No completed downloads yet";
                        emptyDescription.Text = "Completed downloads will appear here.";
                        emptyAdd.Visible = true;
                        break;
                    case 4:
                        emptyTitle.Text = "No errors to show";
                        emptyDescription.Text = "Downloads that need attention will appear here.";
                        emptyAdd.Visible = true;
                        break;
                    default:
                        emptyTitle.Text = "Deleted list is empty";
                        emptyDescription.Text = "Removed downloads will appear here.";
                        emptyAdd.Visible = false;
                        break;
                }
            }
            emptyState.Visible = true;
            emptyState.BringToFront();
            RefreshEmptyStateIcon();
        }

        void RefreshEmptyStateIcon()
        {
            if (emptyIcon == null || emptyIcon.Tag is int oldFilter && oldFilter == filter)
                return;

            var old = emptyIcon.Image;
            emptyIcon.Image = Icons.Make(FilterGlyphs[Math.Clamp(filter, 0, FilterGlyphs.Length - 1)],
                Theme.P.Accent, Theme.Scale(this, 36));
            emptyIcon.Text = Icons.Available ? "" : "\u2193";
            emptyIcon.Tag = filter;
            old?.Dispose();
        }

        // Keeps a copy of a download that is leaving the list, with the time and the reason.
        void RecordDeleted(DownloadItem it, string reason)
        {
            var rec = new DownloadItem
            {
                Url = it.Url,
                FileName = it.FileName,
                Folder = it.Folder,
                Referer = it.Referer,
                UserAgent = it.UserAgent,
                ContentType = it.ContentType,
                TotalBytes = it.TotalBytes,
                SupportsRange = it.SupportsRange,
                Stream = it.Stream,
                YtDlp = it.YtDlp,
                Added = it.Added,
                State = it.State == DlState.Downloading ? DlState.Paused : it.State,
                Error = it.Error,
                DeletedAt = DateTime.Now,
                DeletedReason = reason
            };

            rec.Downloaded = it.Downloaded;

            deleted.Insert(0, rec);

            while (deleted.Count > MaxDeletedHistory)
                deleted.RemoveAt(deleted.Count - 1);
        }

        // "Download again" in the Deleted list: a fresh download from the same link, folder and options.
        void DownloadAgain()
        {
            var sel = Sel().Where(i => i.DeletedAt.HasValue && !string.IsNullOrEmpty(i.Url)).ToList();

            if (sel.Count == 0)
                return;

            if (ThemeDialog.Show(
                    this,
                    "Download " + sel.Count + " item(s) again?",
                    "Deleted",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) != DialogResult.OK)
                return;

            foreach (var d in sel)
            {
                var it = new DownloadItem
                {
                    Url = d.Url,
                    Folder = d.Folder,
                    FileName = d.FileName,
                    Referer = d.Referer,
                    UserAgent = d.UserAgent,
                    ContentType = d.ContentType,
                    TotalBytes = d.TotalBytes,
                    SupportsRange = d.SupportsRange,
                    Stream = d.Stream,
                    YtDlp = d.YtDlp,
                    NeedsProbe = DownloadEngine.IsHttp(d.Url) && d.TotalBytes <= 0,
                    State = DlState.Queued
                };

                deleted.Remove(d);
                items.Add(it);
                selectNewId = it.Id;
            }

            MarkChanged();
            SaveState();

            lblActive.Text = sel.Count + " download(s) added again.";
        }

        void RemoveFromHistory(List<DownloadItem> sel)
        {
            if (ThemeDialog.Show(
                    this,
                    "Remove " + sel.Count + (sel.Count == 1 ? " entry" : " entries") +
                    " from the Deleted list?\n\nFiles on your disk are not touched.",
                    "Deleted",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) != DialogResult.OK)
                return;

            foreach (var d in sel)
                deleted.Remove(d);

            details?.SetItem(null);

            MarkChanged();
            SaveState();
        }

        void ClearHistory()
        {
            if (deleted.Count == 0)
                return;

            if (ThemeDialog.Show(
                    this,
                    "Clear the whole Deleted list (" + deleted.Count + " entries)?\n\nFiles on your disk are not touched.",
                    "Deleted",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) != DialogResult.OK)
                return;

            deleted.Clear();

            details?.SetItem(null);

            MarkChanged();
            SaveState();
        }

        void RebuildList()
        {
            var selected =
                lv.SelectedItems
                  .Cast<ListViewItem>()
                  .Select(l => l.Tag)
                  .OfType<DownloadItem>()
                  .Select(it => it.Id)
                  .ToHashSet();

            lv.BeginUpdate();

            if (lv.Columns.Count > 6)
                lv.Columns[6].Text = filter == DeletedFilter ? "Deleted" : "Added";

            lv.Items.Clear();

            foreach (var it in VisibleItems())
            {
                var lvi =
                    new ListViewItem(
                        new[]
                        {
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            ""
                        })
                    {
                        Tag = it
                    };

                lv.Items.Add(lvi);

                FillRow(lvi, it);

                if (selected.Contains(it.Id))
                    lvi.Selected = true;
            }

            lv.EndUpdate();
            UpdateEmptyState();

            if (selectNewId != null)
            {
                string id = selectNewId;
                selectNewId = null;

                var target =
                    lv.Items
                      .Cast<ListViewItem>()
                      .FirstOrDefault(l => l.Tag is DownloadItem d && d.Id == id);

                if (target != null)
                {
                    foreach (ListViewItem l in lv.SelectedItems.Cast<ListViewItem>().ToList())
                        l.Selected = false;

                    target.Selected = true;
                    target.Focused = true;
                    target.EnsureVisible();
                }
            }

            if (UpdateSideCounts()) sidebar.Invalidate();
        }

        static bool SetIf(
            ListViewItem l,
            int idx,
            string text)
        {
            if (!DownloadRowRefreshPolicy.CellChanged(l.SubItems[idx].Text, text)) return false;
            l.SubItems[idx].Text = text;
            return true;
        }

        static bool FillDeletedRow(
            ListViewItem l,
            DownloadItem it)
        {
            bool changed = SetIf(l, 0, DisplayFileName(it.FileName));

            changed |= SetIf(
                l,
                1,
                it.TotalBytes > 0
                    ? Fmt(it.TotalBytes)
                    : it.Downloaded > 0
                        ? Fmt(it.Downloaded)
                        : "\u2014");

            changed |= SetIf(l, 2, it.Percent.ToString("0.0") + "%");
            changed |= SetIf(l, 3, it.DeletedReason ?? "Deleted");
            changed |= SetIf(l, 4, "");
            changed |= SetIf(l, 5, "");
            var date = it.DeletedAt ?? it.Added;
            changed |= SetIf(l, 6, FormatListDate(date));
            string tooltip = AddListDetailsToTooltip(
                AddDateToTooltip(it.Url, "Deleted", date), it, l.SubItems[3].Text, l.SubItems[1].Text, l.SubItems[4].Text, l.SubItems[5].Text);
            if (l.ToolTipText != tooltip) { l.ToolTipText = tooltip; changed = true; }
            return changed;
        }

        bool FillRow(
            ListViewItem l,
            DownloadItem it)
        {
            if (it.DeletedAt.HasValue)
            {
                return FillDeletedRow(l, it);
            }

            bool changed = SetIf(
                l,
                0,
                DisplayFileName(it.FileName));

            changed |= SetIf(
                l,
                1,
                it.TotalBytes > 0
                    ? (
                        it.Stream != null &&
                        it.State != DlState.Completed
                            ? "~"
                            : "") +
                      Fmt(it.TotalBytes)
                    : "—");

            changed |= SetIf(
                l,
                2,
                it.YtDlp != null && YtDlpRetryPolicy.IsProcessing(it.ProgressStage)
                    ? YtDlpRetryPolicy.StageLabel(it.ProgressStage) + "…"
                    : it.Percent.ToString("0.0") + "%");

            changed |= SetIf(
                l,
                3,
                it.State == DlState.Downloading &&
                !string.IsNullOrEmpty(it.StatusNote)
                    ? it.StatusNote
                    : it.State == DlState.Queued &&
                      scheduleBlocked &&
                      !it.IgnoreSchedule
                        ? "Scheduled"
                        : it.State == DlState.Paused &&
                          it.PausedBySchedule
                            ? "Paused (schedule)"
                            : StateText(it.State));

            changed |= SetIf(
                l,
                4,
                it.State == DlState.Downloading &&
                (it.YtDlp == null || YtDlpRetryPolicy.IsTransferring(it.ProgressStage))
                    ? Fmt(it.Speed) + "/s"
                    : "");

            changed |= SetIf(
                l,
                5,
                Eta(it));

            changed |= SetIf(
                l,
                6,
                FormatListDate(it.Added));

            if (it.State == DlState.Error && it.YtDlp != null)
            {
                var kind = YtDlpDiagnostics.Classify(it.Error);
                var description = YtDlpDiagnostics.Describe(kind, UpdateChecker.IsPackaged);
                var baseTooltip = AppLog.Redact(description.Summary + "\n" + description.NextStep + "\n" + (it.Error ?? ""));
                string fullTooltip = AddListDetailsToTooltip(
                    AddDateToTooltip(baseTooltip, "Added", it.Added), it, l.SubItems[3].Text, l.SubItems[1].Text, l.SubItems[4].Text, l.SubItems[5].Text);
                if (l.ToolTipText != fullTooltip) { l.ToolTipText = fullTooltip; changed = true; }
            }
            else
            {
                var baseTooltip = it.State == DlState.Error ? AppLog.Redact(it.Error) : it.Url;
                string fullTooltip = AddListDetailsToTooltip(
                    AddDateToTooltip(baseTooltip, "Added", it.Added), it, l.SubItems[3].Text, l.SubItems[1].Text, l.SubItems[4].Text, l.SubItems[5].Text);
                if (l.ToolTipText != fullTooltip) { l.ToolTipText = fullTooltip; changed = true; }
            }

            int progressWidth = Math.Max(0, lv.Columns.Count > 2 ? lv.Columns[2].Width - Theme.Scale(this, 58) : 0);
            int fillWidth = it.TotalBytes > 0
                ? Math.Clamp((int)Math.Round(progressWidth * it.Percent / 100.0), 0, progressWidth)
                : 0;
            if (DownloadRowRefreshPolicy.ShouldRedraw(false, l.SubItems[2].Tag, fillWidth))
            {
                l.SubItems[2].Tag = fillWidth;
                changed = true;
            }
            return changed;
        }

        static string AddListDetailsToTooltip(string? tooltip, DownloadItem it, string status, string size, string speed, string eta)
        {
            var details = new List<string>
            {
                "Status: " + status,
                "Size: " + size,
                "Speed: " + speed,
                "ETA: " + eta,
                "Added: " + FormatListDate(it.Added)
            };

            return string.IsNullOrWhiteSpace(tooltip)
                ? string.Join(Environment.NewLine, details)
                : tooltip + Environment.NewLine + string.Join(Environment.NewLine, details);
        }

        static string StateText(DlState s) =>
            s == DlState.Completed
                ? "Complete"
                : s.ToString();

        static string Fmt(double b)
        {
            string[] u =
            {
                "B",
                "KB",
                "MB",
                "GB",
                "TB"
            };

            int i = 0;

            while (b >= 1024 &&
                   i < u.Length - 1)
            {
                b /= 1024;
                i++;
            }

            return (
                i == 0
                    ? b.ToString("0")
                    : b.ToString("0.##"))
                   + " " +
                   u[i];
        }

        static string Eta(DownloadItem it)
        {
            if (it.YtDlp != null && !YtDlpRetryPolicy.IsTransferring(it.ProgressStage))
                return "";
            if (it.State != DlState.Downloading ||
                it.Speed < 1 ||
                it.TotalBytes <= 0)
                return "";

            var t = TimeSpan.FromSeconds(
                (it.TotalBytes - it.Downloaded) /
                it.Speed);

            if (t.TotalHours >= 1)
                return (int)t.TotalHours +
                       "h " +
                       t.Minutes +
                       "m";

            if (t.TotalMinutes >= 1)
                return t.Minutes +
                       "m " +
                       t.Seconds +
                       "s";

            return t.Seconds + "s";
        }

        void SelectAllRows()
        {
            lv.BeginUpdate();

            try
            {
                foreach (ListViewItem l in lv.Items)
                    l.Selected = true;
            }
            finally
            {
                lv.EndUpdate();
            }
        }

        List<DownloadItem> Sel() =>
            lv.SelectedItems
              .Cast<ListViewItem>()
              .Select(l => l.Tag)
              .OfType<DownloadItem>()
              .ToList();

        void MarkChanged()
        {
            dirty = true;
        }
    }
}
