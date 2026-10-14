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
            it.DeletedAt.HasValue ? Gray : StateColor(it.State);

        static Color StateColor(DlState s)
        {
            switch (s)
            {
                case DlState.Downloading:
                    return Theme.P.Accent;

                case DlState.Completed:
                    return Green;

                case DlState.Paused:
                    return Orange;

                case DlState.Error:
                    return Red;

                default:
                    return Gray;
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

        IEnumerable<DownloadItem> VisibleItems() =>
            filter == DeletedFilter
                ? deleted.OrderByDescending(i => i.DeletedAt ?? i.Added)
                : items.Where(i => Match(filter, i)).OrderByDescending(i => i.Added);

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

            if (MessageBox.Show(
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
            if (MessageBox.Show(
                    this,
                    "Remove " + sel.Count + (sel.Count == 1 ? " entry" : " entries") +
                    " from the Deleted list?\n\nFiles on your disk are not touched.",
                    "Deleted",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) != DialogResult.OK)
                return;

            foreach (var d in sel)
                deleted.Remove(d);

            MarkChanged();
            SaveState();
        }

        void ClearHistory()
        {
            if (deleted.Count == 0)
                return;

            if (MessageBox.Show(
                    this,
                    "Clear the whole Deleted list (" + deleted.Count + " entries)?\n\nFiles on your disk are not touched.",
                    "Deleted",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) != DialogResult.OK)
                return;

            deleted.Clear();

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

            UpdateSideCounts();
            sidebar.Invalidate();
        }

        static void SetIf(
            ListViewItem l,
            int idx,
            string text)
        {
            if (l.SubItems[idx].Text != text)
                l.SubItems[idx].Text = text;
        }

        static void FillDeletedRow(
            ListViewItem l,
            DownloadItem it)
        {
            SetIf(l, 0, it.FileName ?? "");

            SetIf(
                l,
                1,
                it.TotalBytes > 0
                    ? Fmt(it.TotalBytes)
                    : it.Downloaded > 0
                        ? Fmt(it.Downloaded)
                        : "\u2014");

            SetIf(l, 2, it.Percent.ToString("0.0") + "%");
            SetIf(l, 3, it.DeletedReason ?? "Deleted");
            SetIf(l, 4, "");
            SetIf(l, 5, "");
            SetIf(l, 6, (it.DeletedAt ?? it.Added).ToString("dd MMM HH:mm"));

            l.ToolTipText = it.Url;
        }

        void FillRow(
            ListViewItem l,
            DownloadItem it)
        {
            if (it.DeletedAt.HasValue)
            {
                FillDeletedRow(l, it);
                return;
            }

            SetIf(
                l,
                0,
                it.FileName);

            SetIf(
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

            SetIf(
                l,
                2,
                it.Percent.ToString("0.0") + "%");

            SetIf(
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

            SetIf(
                l,
                4,
                it.State == DlState.Downloading
                    ? Fmt(it.Speed) + "/s"
                    : "");

            SetIf(
                l,
                5,
                Eta(it));

            SetIf(
                l,
                6,
                it.Added.ToString("dd MMM HH:mm"));

            l.ToolTipText =
                it.State == DlState.Error
                    ? it.Error
                    : it.Url;
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
