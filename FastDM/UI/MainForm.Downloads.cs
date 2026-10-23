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
    // MainForm part: start/pause/resume/remove, open/show-in-folder and the 1-second UI tick.
    public partial class MainForm
    {
        bool IsYouTubeBulkItem(DownloadItem item) => item.YtDlp?.IsBulkItem == true &&
            YouTubeBulkPolicy.IsYouTubeUrl(item.YtDlp.Url ?? item.Url);

        bool CanStartQueuedItem(DownloadItem item, DateTime now)
        {
            int running = items.Count(x => x.State == DlState.Downloading && IsYouTubeBulkItem(x));
            if (!YouTubeBulkPolicy.CanStartBulkItem(IsYouTubeBulkItem(item), running,
                settings.ConcurrentYouTubeDownloads, now, youtubeBackoffUntil,
                lastYouTubeBulkStart, settings.YouTubeItemDelaySeconds))
            {
                item.StatusNote = YouTubeBulkPolicy.WaitingStatus;
                return false;
            }

            if (IsYouTubeBulkItem(item))
            {
                item.StatusNote = "";
                lastYouTubeBulkStart = now;
            }
            return true;
        }

        void RegisterYouTubeFailure(DownloadItem item, YtDlpFailureKind kind)
        {
            if (!IsYouTubeBulkItem(item)) return;
            int seconds = YouTubeBulkPolicy.BackoffSeconds(kind, youtubeBackoffFailures);
            if (seconds <= 0) return;
            youtubeBackoffFailures++;
            youtubeBackoffUntil = DateTime.UtcNow.AddSeconds(seconds);
            foreach (var queued in items.Where(x => x.State == DlState.Queued && IsYouTubeBulkItem(x)))
                queued.StatusNote = YouTubeBulkPolicy.WaitingStatus;
            AppLog.Write("YouTube bulk request backoff: " + seconds + " seconds after " + kind + ".");
        }

        void RegisterYtBatchItem(DownloadItem item)
        {
            string? batchId = item.YtDlp?.BatchId;
            if (string.IsNullOrWhiteSpace(batchId)) return;
            if (!ytBatches.TryGetValue(batchId, out var batch))
            {
                batch = new YtBatchProgress { Skipped = Math.Max(0, item.YtDlp?.BatchSkipped ?? 0) };
                ytBatches.Add(batchId, batch);
            }
            batch.ItemIds.Add(item.Id);
            lastYtBatchId = batchId;
        }

        void UpdateYtBatchProgress(DownloadItem item)
        {
            string? batchId = item.YtDlp?.BatchId;
            if (string.IsNullOrWhiteSpace(batchId) || !ytBatches.TryGetValue(batchId, out var batch)) return;
            if (item.State == DlState.Completed) batch.Completed.Add(item.Id);
            if (item.State == DlState.Error) batch.Failed[item.Id] = item;
            if (batch.ItemIds.Any(id => items.FirstOrDefault(x => x.Id == id)?.State is DlState.Queued or DlState.Downloading or DlState.Paused)) return;

            string text = "Video batch: " + batch.Completed.Count + " done / " + batch.Failed.Count + " failed / " + batch.Skipped + " skipped";
            if (batch.Failed.Count > 0) text += " — Retry failed";
            lblBatchSummary.Text = text;
            lblBatchSummary.Visible = true;
            lblBatchSummary.ToolTipText = batch.Failed.Count > 0 ? "Retry failed downloads from this batch" : text;
        }

        void RetryFailedYtBatch()
        {
            if (lastYtBatchId == null || !ytBatches.TryGetValue(lastYtBatchId, out var batch) || batch.Failed.Count == 0) return;
            foreach (var item in batch.Failed.Values.Where(x => x.State == DlState.Error))
            {
                item.State = DlState.Queued;
                item.Error = null;
                item.StatusNote = "";
                item.RetryCount = 0;
            }
            batch.Failed.Clear();
            lblBatchSummary.Visible = false;
            MarkChanged();
            SaveState();
        }

        // ---------- টাইমার: কিউ + স্পিড ----------
        void OnTick(
            object? sender,
            EventArgs e)
        {
            bool schedOn =
                settings.SchedulerEnabled;

            bool allowed =
                !schedOn ||
                Scheduler.IsAllowed(
                    settings,
                    DateTime.Now);

            scheduleBlocked =
                schedOn &&
                !allowed;

            if (lastAllowed != allowed)
            {
                lastAllowed = allowed;

                if (!allowed)
                {
                    if (settings.ScheduleStopOutside)
                    {
                        foreach (var it in items
                            .Where(i =>
                                i.State ==
                                    DlState.Downloading &&
                                !i.IgnoreSchedule)
                            .ToList())
                        {
                            it.PausedBySchedule = true;
                            it.Cts?.Cancel();
                        }
                    }
                }
                else
                {
                    foreach (var it in items
                        .Where(i =>
                            i.PausedBySchedule &&
                            i.State == DlState.Paused)
                        .ToList())
                    {
                        it.PausedBySchedule = false;
                        it.State = DlState.Queued;
                    }
                }

                MarkChanged();
            }

            int active =
                items.Count(
                    i => i.State ==
                         DlState.Downloading);

            int availableSlots = DownloadQueuePolicy.AvailableSlots(active, settings.MaxSimultaneous);
            foreach (var it in items
                .Where(i => i.State == DlState.Queued && (!scheduleBlocked || i.IgnoreSchedule))
                .OrderBy(i => i.Added)
                .ToList())
            {
                if (availableSlots <= 0) break;
                if (!CanStartQueuedItem(it, DateTime.UtcNow)) continue;
                StartDownload(it);
                active++;
                availableSlots--;
            }

            var now = DateTime.UtcNow;
            double total = 0;

            foreach (var it in items)
            {
                if (it.State ==
                    DlState.Downloading)
                {
                    double dt =
                        (now - it.LastTick)
                            .TotalSeconds;

                    if (dt >= 0.4)
                    {
                        long cur =
                            it.Downloaded;

                        double inst =
                            (cur - it.LastBytes) /
                            dt;

                        it.Speed =
                            it.Speed <= 0
                                ? inst
                                : it.Speed * 0.6 +
                                  inst * 0.4;

                        it.LastBytes = cur;
                        it.LastTick = now;
                    }

                    total += it.Speed;

                    it.SpeedHistory.Add(
                        it.Speed);

                    if (it.SpeedHistory.Count >
                        DetailsPanel.HistoryLength)
                    {
                        it.SpeedHistory.RemoveAt(0);
                    }
                }
                else
                {
                    it.Speed = 0;
                }
            }

            if (dirty)
            {
                dirty = false;
                RebuildList();
            }
            else
            {
                for (int row = 0; row < lv.Items.Count; row++)
                {
                    var l = lv.Items[row];
                    if (l.Tag is DownloadItem item && FillRow(l, item))
                        lv.RedrawItems(row, row, invalidateOnly: true);
                }
            }

            int actNow =
                items.Count(
                    i => i.State ==
                         DlState.Downloading);

            int queNow =
                items.Count(
                    i => i.State ==
                         DlState.Queued);

            string activeText = "Active: " + actNow + "   Queued: " + queNow;
            if (youtubeBackoffUntil > DateTime.UtcNow)
                activeText += "   " + YouTubeBulkPolicy.WaitingStatus;
            if (lblActive.Text != activeText) lblActive.Text = activeText;

            string speedText = "↓ " + Fmt(total) + "/s";
            if (lblSpeed.Text != speedText) lblSpeed.Text = speedText;

            string limText =
                settings.SpeedLimitKBps > 0
                    ? "Limit: " + SpeedLimiter.Describe(settings.SpeedLimitKBps)
                    : "Limit: Off";

            if (lblLimit.Text != limText)
                lblLimit.Text = limText;

            string schedText =
                Scheduler.Status(
                    settings,
                    DateTime.Now,
                    allowed);

            if (afterAll !=
                PostAction.None)
            {
                schedText +=
                    "   When done: " +
                    PowerActions.Name(afterAll);
            }

            if (lblSched.Text != schedText)
                lblSched.Text = schedText;

            if (details.Visible) details.RefreshIfChanged();

            PeriodicMaintenance();
            PauseSlowDownloads();

            if (UpdateSideCounts()) sidebar.Invalidate();

            string tip =
                actNow > 0
                    ? "FastDM — ↓ " +
                      Fmt(total) +
                      "/s"
                    : "FastDM";

            if (tray != null &&
                tip != lastTrayTip)
            {
                tray.Text = tip;
                lastTrayTip = tip;
            }

            if (sessionCompleted > 0 &&
                actNow == 0 &&
                queNow == 0)
            {
                if (settings.NotifyCompleted)
                    Notify(
                        "Download complete",
                        sessionCompleted == 1
                            ? lastCompletedName
                            : sessionCompleted +
                              " downloads completed");

                sessionCompleted = 0;

                RunAfterAllAction();
            }

            if (active > 0 &&
                (now - lastSave).TotalSeconds > 3)
            {
                SaveState();
            }
        }

        // ---------- ডাউনলোড কন্ট্রোল ----------
        async void StartDownload(
            DownloadItem it)
        {
            try
            {
                var cts =
                    new CancellationTokenSource();

                it.Cts = cts;
                it.State = DlState.Downloading;
                it.Error = null;
                it.PausedBySchedule = false;

                // ম্যানুয়াল শুরু/রিজিউমে হলে অটো-রিট্রাইয়ের গণনা নতুন করে
                if (!it.AutoRetrying)
                    it.RetryCount = 0;

                it.AutoRetrying = false;
                it.SlowSince = null;

                AppLog.Write(
                    "Start: " +
                    it.FileName);

                it.SpeedHistory.Clear();
                it.LastBytes = it.Downloaded;
                it.LastTick = DateTime.UtcNow;
                it.Speed = 0;

                MarkChanged();
                YtDlpFailureKind? ytFailureKind = null;
                TimeSpan? ytRetryAfter = null;

                try
                {
                    if (it.YtDlp != null)
                    {
                        await YtDlpEngine.RunAsync(
                            it,
                            settings.Connections,
                            cts.Token);
                    }
                    else if (it.Stream != null)
                    {
                        await StreamEngine.RunAsync(
                            it,
                            settings.Connections,
                            cts.Token);
                    }
                    else
                    {
                        await DownloadEngine.RunItemAsync(
                            it,
                            settings.Connections,
                            cts.Token);
                    }

                    if (it.TotalBytes <= 0)
                        it.TotalBytes =
                            it.Downloaded;

                    it.State =
                        DlState.Completed;
                }
                catch (OperationCanceledException)
                {
                    it.State =
                        DlState.Paused;
                }
                catch (Exception ex)
                {
                    it.State =
                        DlState.Error;

                    if (it.YtDlp != null)
                    {
                        ytFailureKind = ex is YtDlpProcessException ytException
                            ? ytException.FailureKind
                            : YtDlpDiagnostics.Classify(ex.Message);
                        ytRetryAfter = (ex as YtDlpProcessException)?.RetryAfter;
                        RegisterYouTubeFailure(it, ytFailureKind.Value);
                    }

                    it.Error =
                        AppLog.Redact(ex.GetBaseException().Message);

                    AppLog.Write(
                        "Error: " +
                        it.FileName +
                        " — " +
                        AppLog.Redact(ex.GetBaseException().Message));
                }
                finally
                {
                    it.Cts = null;
                    it.StatusNote = "";
                    if (it.State is DlState.Completed or DlState.Paused or DlState.Error)
                    {
                        it.ProgressStage = YtDlpStage.None;
                        it.Speed = 0;
                    }
                    cts.Dispose();
                }

                // ধীর বলে সরানো ডাউনলোড: লাইনের শেষে গিয়ে অপেক্ষা করবে, অন্যগুলো আগে চলবে
                if (it.RequeueAfterPause)
                {
                    it.RequeueAfterPause = false;

                    if (it.State ==
                            DlState.Paused &&
                        !it.RemoveRequested)
                    {
                        it.Added =
                            DateTime.Now;

                        it.State =
                            DlState.Queued;
                    }
                }

                if (!it.RemoveRequested)
                {
                    if (it.State ==
                        DlState.Completed)
                    {
                        sessionCompleted++;
                        lastCompletedName =
                            it.FileName;

                        FinishDownload(it);
                    }
                    else if (it.State ==
                             DlState.Error)
                    {
                        bool retryableVideoFailure = it.YtDlp == null ||
                            YtDlpRetryPolicy.ShouldAutoRetry(
                                ytFailureKind ?? YtDlpDiagnostics.Classify(it.Error),
                                it.RetryCount,
                                ytRetryAfter);
                        if (settings.AutoRetry && retryableVideoFailure &&
                            it.RetryCount <
                                Math.Max(1, settings.MaxRetries))
                        {
                            it.RetryCount++;
                            ScheduleRetry(it, ytRetryAfter);
                        }
                        else
                        {
                            NotifyError(it);
                        }
                    }
                }

                UpdateYtBatchProgress(it);

                it.Speed = 0;

                if (it.RemoveRequested)
                {
                    TryDelete(it.TempPath);
                    TryDelete(it.PartsDir);
                    YtDlpEngine.Cleanup(it);

                    if (it.State ==
                            DlState.Completed &&
                        it.DeleteFile)
                    {
                        TryDelete(it.SavePath);
                    }
                }

                MarkChanged();
                SaveState();
            }
            catch (OperationCanceledException)
            {
                AppLog.Write("MainForm.StartDownload: cancelled");
            }
            catch (Exception exGuard)
            {
                Program.ReportError("MainForm.StartDownload", exGuard);
            }
        }

        // ডাউনলোড শেষের কাজ: সার্ভারের সময়, Mark of the Web, অ্যান্টিভাইরাস, বাইরের অ্যাপ, লিস্ট থেকে সরানো
        void FinishDownload(
            DownloadItem it)
        {
            it.RetryCount = 0;

            AppLog.Write(
                "Completed: " +
                it.SavePath);

            var s = settings;

            // ডিস্ক আর বাইরের প্রোগ্রাম UI আটকাবে না
            Task.Run(
                () => PostDownload.Apply(
                    s,
                    it));

            if (s.AutoRemoveCompleted)
                items.Remove(it);
        }

        // ব্যর্থ ডাউনলোড কিছুক্ষণ পর নিজে আবার চেষ্টা (৫, ১০, ১৫… সেকেন্ড, সর্বোচ্চ ৬০)
        async void ScheduleRetry(
            DownloadItem it,
            TimeSpan? retryAfter = null)
        {
            try
            {
                AppLog.Write(
                    "Retry " +
                    it.RetryCount +
                    ": " +
                    it.FileName);

                TimeSpan delay = retryAfter ?? TimeSpan.FromSeconds(Math.Min(60, 5 * it.RetryCount));
                await Task.Delay(delay);

                if (items.Contains(it) &&
                    it.State ==
                        DlState.Error &&
                    !it.RemoveRequested)
                {
                    it.AutoRetrying = true;
                    it.State =
                        DlState.Queued;
                    it.Error = null;

                    MarkChanged();
                }
            }
            catch (OperationCanceledException)
            {
                AppLog.Write("MainForm.ScheduleRetry: cancelled");
            }
            catch (Exception exGuard)
            {
                Program.ReportError("MainForm.ScheduleRetry", exGuard);
            }
        }

        // "Pause slow downloads and let others go first": অন্য ডাউনলোড লাইনে অপেক্ষা করলে
        // এবং এটা নির্ধারিত সময় ধরে সীমার চেয়ে ধীর হলে সরিয়ে লাইনের শেষে পাঠায়
        void PauseSlowDownloads()
        {
            if (!settings.PauseSlow)
                return;

            var now =
                DateTime.UtcNow;

            double limit =
                settings.PauseSlowKBps *
                1024.0;

            int waiting =
                items.Count(
                    i => i.State ==
                             DlState.Queued &&
                         (!scheduleBlocked ||
                          i.IgnoreSchedule));

            foreach (var it in items
                .Where(i =>
                    i.State ==
                    DlState.Downloading)
                .ToList())
            {
                if (it.Speed > limit)
                {
                    it.SlowSince = null;
                    continue;
                }

                if (it.SlowSince == null)
                {
                    it.SlowSince = now;
                    continue;
                }

                if (waiting > 0 &&
                    it.Cts != null &&
                    (now - it.SlowSince.Value)
                        .TotalMinutes >=
                    Math.Max(1, settings.PauseSlowMinutes))
                {
                    AppLog.Write(
                        "Slow download moved back: " +
                        it.FileName);

                    it.SlowSince = null;
                    it.RequeueAfterPause = true;
                    it.Cts?.Cancel();

                    waiting--;
                }
            }
        }

        void PeriodicMaintenance()
        {
            if (!settings.AutoRemoveMissing)
                return;

            if ((DateTime.UtcNow -
                 lastMissingCheck)
                    .TotalSeconds < 30)
                return;

            lastMissingCheck =
                DateTime.UtcNow;

            var gone =
                items
                    .Where(i =>
                        i.State ==
                            DlState.Completed &&
                        FileCheck.IsDeleted(
                            i.Folder,
                            i.FileName))
                    .ToList();

            if (gone.Count == 0)
                return;

            foreach (var g in gone)
            {
                RecordDeleted(g, "File deleted from disk");
                items.Remove(g);
            }

            AppLog.Write(
                "Moved " +
                gone.Count +
                " missing file(s) to the Deleted list");

            MarkChanged();
            SaveState();
        }

        void ResumeSelected()
        {
            if (filter == DeletedFilter)
                return;

            foreach (var it in Sel())
            {
                if (it.State ==
                        DlState.Paused ||
                    it.State ==
                        DlState.Error)
                {
                    it.State = DlState.Queued;
                    it.Error = null;
                }
            }

            MarkChanged();
        }

        void PauseSelected()
        {
            if (filter == DeletedFilter)
                return;

            foreach (var it in Sel())
            {
                if (it.State ==
                    DlState.Downloading)
                {
                    it.Cts?.Cancel();
                }
                else if (it.State ==
                         DlState.Queued)
                {
                    it.State =
                        DlState.Paused;
                }
            }

            MarkChanged();
        }

        void PauseAll()
        {
            foreach (var it in items)
            {
                if (it.State ==
                    DlState.Downloading)
                {
                    it.Cts?.Cancel();
                }
                else if (it.State ==
                         DlState.Queued)
                {
                    it.State =
                        DlState.Paused;
                }
            }

            MarkChanged();
        }

        void ResumeAll()
        {
            foreach (var it in items)
            {
                if (it.State ==
                        DlState.Paused ||
                    it.State ==
                        DlState.Error)
                {
                    it.State =
                        DlState.Queued;

                    it.Error = null;
                }
            }

            MarkChanged();
        }

        void RemoveSelected()
        {
            var sel = Sel();

            if (sel.Count == 0)
                return;

            // in the Deleted list, Remove forgets the history entries
            if (filter == DeletedFilter)
            {
                RemoveFromHistory(sel);
                return;
            }

            bool deleteDone = false;

            // Preferences → Advanced → Delete button action
            if (settings.DeleteAction !=
                DeleteAction.Ask)
            {
                deleteDone =
                    settings.DeleteAction ==
                        DeleteAction.DeleteFiles &&
                    sel.Any(
                        i => i.State ==
                             DlState.Completed);
            }
            else if (sel.Any(
                    i => i.State ==
                         DlState.Completed))
            {
                var r = ThemeDialog.Show(
                    this,
                    "Remove " +
                    sel.Count +
                    " download(s) from the list?\n\n" +
                    "Yes = remove from list only\n" +
                    "No = remove AND delete completed file(s) from disk",
                    "Remove",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (r == DialogResult.Cancel)
                    return;

                deleteDone =
                    r == DialogResult.No;
            }
            else
            {
                if (ThemeDialog.Show(
                        this,
                        "Remove " +
                        sel.Count +
                        " download(s)? Partial data will be deleted.",
                        "Remove",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question)
                    != DialogResult.OK)
                {
                    return;
                }
            }

            foreach (var it in sel)
            {
                it.RemoveRequested = true;
                it.DeleteFile = deleteDone;

                RecordDeleted(
                    it,
                    it.State == DlState.Completed
                        ? (deleteDone ? "Removed, file deleted from disk" : "Removed from the list")
                        : "Removed before it finished");

                items.Remove(it);

                if (it.State ==
                    DlState.Downloading)
                {
                    it.Cts?.Cancel();
                }
                else
                {
                    TryDelete(it.TempPath);
                    TryDelete(it.PartsDir);

                    YtDlpEngine.Cleanup(it);

                    if (it.State ==
                            DlState.Completed &&
                        deleteDone)
                    {
                        TryDelete(it.SavePath);
                    }
                }
            }

            details?.SetItem(null);

            MarkChanged();
            SaveState();
        }

        static void TryDelete(
            string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                else if (Directory.Exists(path))
                    Directory.Delete(
                        path,
                        true);
            }
            catch (Exception exIgnored1) { AppLog.Write("MainForm.Downloads (ignored): " + exIgnored1.Message); }
        }

        void OpenSelected(bool folder)
        {
            var sel = Sel();

            if (sel.Count == 0)
                return;

            var it = sel[0];

            try
            {
                if (folder)
                {
                    RevealInExplorer(it);
                }
                else
                {
                    if (it.DeletedAt.HasValue &&
                        !File.Exists(it.SavePath))
                    {
                        ThemeDialog.Show(
                            this,
                            "This file was deleted from the disk.");

                        return;
                    }

                    if (it.State !=
                            DlState.Completed ||
                        !File.Exists(
                            it.SavePath))
                    {
                        ThemeDialog.Show(
                            this,
                            "File is not completed yet.");

                        return;
                    }

                    if (FileCheck.IsRiskyToOpen(it.SavePath) &&
                        ThemeDialog.Show(
                            this,
                            "This is a program or script file. Run it only if you trust where it came from.\n\nOpen it anyway?",
                            "FastDM",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        return;

                    Process.Start(
                        new ProcessStartInfo(
                            it.SavePath)
                        {
                            UseShellExecute = true
                        });
                }
            }
            catch (Exception ex)
            {
                ThemeDialog.ShowError(this, "FastDM", "Couldn't open the selected file.", ex);
            }
        }

        // Opens Explorer on the downloaded file (or on its folder when the file is not there yet).
        // Returns false when there is nothing to open.
        static bool RevealInExplorer(DownloadItem it)
        {
            string? target =
                File.Exists(it.SavePath)
                    ? it.SavePath
                    : File.Exists(it.TempPath)
                        ? it.TempPath
                        : null;

            if (target != null)
            {
                Process.Start(
                    "explorer.exe",
                    "/select,\"" + target + "\"");

                return true;
            }

            if (!string.IsNullOrEmpty(it.Folder) && Directory.Exists(it.Folder))
            {
                Process.Start(
                    "explorer.exe",
                    "\"" + it.Folder + "\"");

                return true;
            }

            return false;
        }

        // Click on "Save to" in the details panel
        void OpenFolderFromDetails(DownloadItem it)
        {
            try
            {
                if (!RevealInExplorer(it))
                {
                    ThemeDialog.Show(
                        this,
                        "This folder does not exist (yet):\n" + it.Folder,
                        "FastDM");
                }
            }
            catch (Exception ex)
            {
                ThemeDialog.ShowError(this, "FastDM", "Couldn't open the selected folder.", ex);
            }
        }

        // Click on "Link" in the details panel: open the page in the default browser (web links only)
        void OpenLinkFromDetails(DownloadItem it)
        {
            string url =
                PlaylistBuilder.StripCredentials(
                    it.YtDlp?.Url ?? it.Url ?? "");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
                (u.Scheme != Uri.UriSchemeHttp &&
                 u.Scheme != Uri.UriSchemeHttps))
                return;

            try
            {
                Process.Start(
                    new ProcessStartInfo(u.AbsoluteUri)
                    {
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                ThemeDialog.ShowError(this, "FastDM", "Couldn't open the selected link.", ex);
            }
        }

        void OnDoubleClick()
        {
            var sel = Sel();

            if (sel.Count == 0)
                return;

            var it = sel[0];

            if (it.DeletedAt.HasValue)
            {
                DownloadAgain();
                return;
            }

            if (it.State ==
                DlState.Completed)
            {
                OpenSelected(false);
            }
            else if (
                it.State ==
                DlState.Downloading)
            {
                PauseSelected();
            }
            else
            {
                ResumeSelected();
            }
        }
    }
}
