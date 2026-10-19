using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FastDM
{
    sealed class YtPickerForm : Form
    {
        sealed class OptItem
        {
            public string Text = "";
            public object? Tag;
            public override string ToString() => Text;
        }

        sealed class PlaylistItem
        {
            public YtPlaylistEntry Entry = null!;
            public override string ToString()
            {
                string duration = Entry.Duration > 0 ? "  •  " + TimeSpan.FromSeconds(Entry.Duration).ToString(@"m\:ss") : "";
                return Entry.Index + ". " + (Entry.Available ? Entry.Title : "Unavailable: " + Entry.Title) + duration;
            }
        }

        sealed class ThumbnailFetchResult
        {
            public byte[]? Bytes;
            public string Host = "unknown";
            public string Reason = "unknown";
            public System.Net.HttpStatusCode? Status;
            public bool IsWebp;
        }

        readonly string url;
        readonly int maxPlaylistEntries;
        readonly bool ignoreCookies;
        CancellationTokenSource previewCts = new CancellationTokenSource();
        readonly Font mergeFont;
        readonly Label lblTitle, lblSource, lblStatus, lblSize, lblMerge, lblCount, thumbnailPlaceholder;
        readonly Label lblPlaylistQuality, lblName;
        readonly PictureBox thumbnail;
        readonly ThumbnailSkeleton thumbnailSkeleton;
        readonly ThemedMarqueeProgress loadingProgress;
        readonly Panel singlePanel, playlistPanel;
        readonly TableLayoutPanel options;
        readonly RadioButton rbVideo, rbAudio;
        readonly ScreenBoundComboBox cmbVideo, cmbAudio, cmbPlaylistQuality;
        readonly CheckedListBox playlistEntries;
        readonly TextBox txtName, txtFolder;
        readonly Button btnStart, btnLater;
        readonly Button btnRetry;
        YtInfo? info;
        YtPlaylistInfo? playlist;
        YtPreviewData? preview;
        bool loading;

        public List<YtDlpSpec> Specs { get; private set; } = new List<YtDlpSpec>();
        public string? ErrorMessage { get; private set; }
        public string SaveFolder => txtFolder.Text.Trim();

        public YtPickerForm(string videoUrl, string defaultFolder, int maxPlaylistEntries = 100, bool ignoreCookies = false)
        {
            Theme.ConfigureForm(this);
            url = videoUrl;
            this.maxPlaylistEntries = Math.Clamp(maxPlaylistEntries, 1, 1000);
            this.ignoreCookies = ignoreCookies;
            Text = "Video download preview";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Theme.CreateFont(Theme.FontBody);
            mergeFont = Theme.CreateFont(Theme.FontSmall);
            ClientSize = new Size(760, 650);
            MinimumSize = new Size(660, 590);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 1,
                RowCount = 6
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));

            lblTitle = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font(Font, FontStyle.Bold),
                Text = "",
                AccessibleName = "Video title"
            };
            root.Controls.Add(lblTitle, 0, 0);

            var previewPanel = new Panel { Dock = DockStyle.Fill };
            thumbnail = new PictureBox
            {
                Location = new Point(0, 2),
                Size = new Size(144, 82),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Video thumbnail"
            };
            previewPanel.Controls.Add(thumbnail);
            thumbnailSkeleton = new ThumbnailSkeleton
            {
                Location = new Point(0, 2),
                Size = new Size(144, 82),
                BackColor = Theme.P.Hover
            };
            previewPanel.Controls.Add(thumbnailSkeleton);
            thumbnailPlaceholder = new ThumbnailPlaceholderLabel
            {
                Location = new Point(0, 2),
                Size = new Size(144, 82),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.CreateFont(24, FontStyle.Bold),
                ForeColor = Theme.P.SubText,
                BackColor = Theme.P.Header,
                Visible = false,
                AccessibleName = "Video thumbnail placeholder"
            };
            previewPanel.Controls.Add(thumbnailPlaceholder);
            lblSource = new Label
            {
                Location = new Point(158, 2),
                Size = new Size(540, 80),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                Text = "Reading title, channel, duration, formats, and size…",
                AccessibleName = "Video source and details"
            };
            previewPanel.Controls.Add(lblSource);
            root.Controls.Add(previewPanel, 0, 1);

            var statusPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = Padding.Empty
            };
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            statusPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
            lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Opening video preview…",
                AutoEllipsis = true,
                AccessibleName = "Preview loading status",
                AccessibleDescription = "Shows the current video preview state."
            };
            statusPanel.Controls.Add(lblStatus, 0, 0);
            btnRetry = new Button
            {
                Text = "Retry",
                Dock = DockStyle.Fill,
                Visible = false,
                AccessibleName = "Retry video preview",
                AccessibleDescription = "Try loading the video information again."
            };
            btnRetry.Click += (s, e) => _ = LoadPreviewAsync();
            statusPanel.Controls.Add(btnRetry, 1, 0);
            loadingProgress = new ThemedMarqueeProgress
            {
                Dock = DockStyle.Fill,
                AccessibleName = "Video preview loading progress",
                AccessibleDescription = "Indeterminate progress while video information is loading."
            };
            statusPanel.Controls.Add(loadingProgress, 0, 1);
            statusPanel.SetColumnSpan(loadingProgress, 2);
            root.Controls.Add(statusPanel, 0, 2);

            var mainPanel = new Panel { Dock = DockStyle.Fill };
            singlePanel = new Panel { Dock = DockStyle.Fill };
            playlistPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
            mainPanel.Controls.Add(playlistPanel);
            mainPanel.Controls.Add(singlePanel);
            root.Controls.Add(mainPanel, 0, 3);

            rbVideo = new RadioButton { Text = "Video + audio", Location = new Point(0, 4), AutoSize = true, Checked = true };
            rbAudio = new RadioButton { Text = "Audio only", Location = new Point(160, 4), AutoSize = true };
            rbAudio.CheckedChanged += (s, e) => UpdateSingleMode();
            singlePanel.Controls.Add(rbVideo);
            singlePanel.Controls.Add(rbAudio);
            singlePanel.Controls.Add(new Label { Text = "Format and quality", Location = new Point(0, 38), AutoSize = true });
            cmbVideo = new ScreenBoundComboBox { Location = new Point(0, 60), Width = 690, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbVideo.SelectedIndexChanged += (s, e) => UpdateSizeAndMerge();
            singlePanel.Controls.Add(cmbVideo);
            cmbAudio = new ScreenBoundComboBox { Location = new Point(0, 60), Width = 690, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
            cmbAudio.Items.Add(new OptItem { Text = "M4A — best available audio", Tag = "m4a" });
            cmbAudio.Items.Add(new OptItem { Text = "MP3 — converted audio", Tag = "mp3" });
            cmbAudio.SelectedIndex = 0;
            singlePanel.Controls.Add(cmbAudio);
            lblSize = new Label { Location = new Point(0, 96), Size = new Size(690, 24), AutoEllipsis = true };
            singlePanel.Controls.Add(lblSize);
            lblMerge = new Label { Location = new Point(0, 122), Size = new Size(690, 24), AutoEllipsis = true, Font = mergeFont, ForeColor = Theme.P.SubText };
            singlePanel.Controls.Add(lblMerge);

            lblCount = new Label { Dock = DockStyle.Top, Height = 26, Text = "Loading playlist entries…", AutoEllipsis = true };
            playlistPanel.Controls.Add(lblCount);
            playlistEntries = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                AccessibleName = "Playlist videos"
            };
            playlistEntries.ItemCheck += OnPlaylistItemCheck;
            playlistEntries.SelectedIndexChanged += (s, e) => UpdatePlaylistCount();
            playlistPanel.Controls.Add(playlistEntries);
            var playlistHelp = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                Text = "Select the available videos to add. Playlist downloads use each video's title as its file name.",
                AutoEllipsis = true
            };
            playlistPanel.Controls.Add(playlistHelp);

            options = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(0, 4, 0, 0)
            };
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            lblPlaylistQuality = new Label { Text = "Playlist quality", AutoSize = true, Visible = false };
            options.Controls.Add(lblPlaylistQuality, 0, 0);
            cmbPlaylistQuality = new ScreenBoundComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPlaylistQuality.Items.AddRange(BatchQuality.Presets);
            cmbPlaylistQuality.SelectedIndex = 0;
            cmbPlaylistQuality.Visible = false;
            options.Controls.Add(cmbPlaylistQuality, 0, 1);
            lblName = new Label { Text = "File name (single video)", AutoSize = true, Visible = false };
            options.Controls.Add(lblName, 0, 2);
            txtName = new TextBox { Dock = DockStyle.Fill, Visible = false, AccessibleName = "Output file name" };
            options.Controls.Add(txtName, 0, 3);
            root.Controls.Add(options, 0, 4);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var folderRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
            folderRow.Controls.Add(new Label { Text = "Save to", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
            txtFolder = new TextBox { Text = defaultFolder, Width = 520, Anchor = AnchorStyles.Left | AnchorStyles.Right, AccessibleName = "Save folder" };
            folderRow.Controls.Add(txtFolder);
            var browse = new Button { Text = "Browse…", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
            browse.Click += (s, e) =>
            {
                using var dialog = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (dialog.ShowDialog(this) == DialogResult.OK) txtFolder.Text = dialog.SelectedPath;
            };
            folderRow.Controls.Add(browse);
            footer.Controls.Add(folderRow, 0, 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
            var cancel = new Button { Text = "Cancel", Size = new Size(92, 34), DialogResult = DialogResult.Cancel };
            cancel.Click += (s, e) =>
            {
                DialogResult = ErrorMessage == null ? DialogResult.Cancel : DialogResult.Abort;
                Close();
            };
            btnLater = new Button { Text = "Download Later", Size = new Size(128, 34), Enabled = false };
            btnStart = new Button { Text = "Start download", Size = new Size(128, 34), Enabled = false };
            btnStart.Click += (s, e) => Accept(false);
            btnLater.Click += (s, e) => Accept(true);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(btnLater);
            buttons.Controls.Add(btnStart);
            footer.Controls.Add(buttons, 0, 1);
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.Controls.Add(footer, 0, 5);

            Controls.Add(root);
            Resize += (s, e) => UpdateComboDropdownWidths();
            SetOptionsMode(isPlaylist: false, hasPreview: false);
            SetPreviewControlsEnabled(false);
            AcceptButton = btnStart;
            CancelButton = cancel;
            FormClosing += (s, e) =>
            {
                previewCts.Cancel();
                if (ErrorMessage != null && DialogResult == DialogResult.None)
                    DialogResult = DialogResult.Abort;
            };
            FormClosed += (s, e) =>
            {
                thumbnail.Image?.Dispose();
                thumbnailPlaceholder.Font.Dispose();
                previewCts.Dispose();
                mergeFont.Dispose();
            };
            Shown += async (s, e) => await LoadPreviewAsync();
        }

        async Task LoadPreviewAsync()
        {
            if (loading || IsDisposed) return;
            loading = true;
            ErrorMessage = null;
            Text = "Video download preview";
            lblTitle.Text = "";
            lblStatus.Text = "Loading video information…";
            btnRetry.Visible = false;
            loadingProgress.Visible = true;
            btnStart.Enabled = false;
            btnLater.Enabled = false;
            SetPreviewControlsEnabled(false);
            var previousImage = thumbnail.Image;
            thumbnail.Image = null;
            previousImage?.Dispose();
            thumbnailSkeleton.Visible = true;
            thumbnailPlaceholder.Visible = false;
            info = null;
            playlist = null;
            preview = null;
            previewCts.Cancel();
            previewCts.Dispose();
            previewCts = new CancellationTokenSource();

            try
            {
                previewCts.CancelAfter(TimeSpan.FromSeconds(90));
                var progress = new Progress<string>(text =>
                {
                    if (!IsDisposed) lblStatus.Text = text;
                });

                bool cacheHit = YtDlpPreviewCache.Shared.TryGet(url, DateTimeOffset.UtcNow, out preview, ignoreCookies) && preview != null;
                if (cacheHit && preview?.Playlist is YtPlaylistInfo cachedPlaylist && cachedPlaylist.EntriesLimit > 0 && cachedPlaylist.EntriesLimit < maxPlaylistEntries)
                {
                    YtDlpPreviewCache.Shared.Remove(url, ignoreCookies);
                    preview = null;
                    cacheHit = false;
                }
                if (!cacheHit || preview == null)
                {
                    if (YtDlpMetadataParser.IsYouTubePlaylistUrl(url))
                    {
                        playlist = await YtDlpInfo.LoadPlaylistAsync(url, previewCts.Token, progress, maxPlaylistEntries, ignoreCookies);
                        preview = new YtPreviewData { Playlist = playlist };
                    }
                    else
                    {
                        info = await YtDlpInfo.LoadAsync(url, previewCts.Token, progress, ignoreCookies);
                        preview = new YtPreviewData { Video = info };
                    }
                    YtDlpPreviewCache.Shared.Store(url, preview, DateTimeOffset.UtcNow, ignoreCookies);
                }
                else
                {
                    info = preview.Video;
                    playlist = preview.Playlist;
                }

                previewCts.CancelAfter(Timeout.InfiniteTimeSpan);
                if (IsDisposed) return;
                loading = false;
                SetPreviewControlsEnabled(true);
                RenderPreview();
                bool canAccept = playlist != null
                    ? playlist.AvailableCount > 0
                    : cmbVideo.Items.Count > 0 || rbAudio.Enabled;
                if (canAccept)
                {
                    btnStart.Enabled = true;
                    btnLater.Enabled = true;
                    lblStatus.Text = playlist != null
                        ? "Preview ready. Playlist sizes are unknown until each video is analyzed."
                        : "Preview ready. Choose a format and destination.";
                }
                else
                {
                    ErrorMessage = FfmpegLocator.Find() == null
                        ? "No available video format can be saved without FFmpeg. Install a trusted FFmpeg build beside FastDM, then reopen the preview."
                        : "yt-dlp returned no downloadable video or audio formats.";
                    lblStatus.Text = ErrorMessage;
                    Text = "No compatible video format";
                }
                loadingProgress.Visible = false;
                btnRetry.Visible = !canAccept;
                if (!canAccept) SetPreviewControlsEnabled(false);
                if (info?.Thumbnail is string imageUrl)
                    _ = LoadThumbnailAsync(imageUrl, previewCts.Token, info.ThumbnailNeedsYtDlpConversion);
                else
                {
                    ShowThumbnailPlaceholder();
                    LogThumbnailFailure(ThumbnailHost(url), null, "thumbnail_candidate_missing");
                }
            }
            catch (OperationCanceledException) when (previewCts.IsCancellationRequested)
            {
                if (IsDisposed || Disposing) return;
                loading = false;
                ErrorMessage = "Reading video information timed out or was cancelled.";
                lblStatus.Text = "The preview timed out. Check the link or connection, then retry.";
                btnStart.Enabled = false;
                btnLater.Enabled = false;
                SetPreviewControlsEnabled(false);
                loadingProgress.Visible = false;
                btnRetry.Visible = true;
                Text = "Video preview could not be loaded";
                ShowThumbnailPlaceholder();
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing) return;
                loading = false;
                string message = AppLog.Redact(ex.GetBaseException().Message);
                string diagnostic = ex is YtDlpProcessException processException && !string.IsNullOrWhiteSpace(processException.DiagnosticText)
                    ? processException.DiagnosticText
                    : message;
                ErrorMessage = AppLog.Redact(diagnostic);
                lblStatus.Text = "Couldn't load video information. Check the link or connection, then retry.";
                btnStart.Enabled = false;
                btnLater.Enabled = false;
                SetPreviewControlsEnabled(false);
                loadingProgress.Visible = false;
                btnRetry.Visible = true;
                Text = "Video preview could not be loaded";
                ShowThumbnailPlaceholder();
            }
        }

        void SetPreviewControlsEnabled(bool enabled)
        {
            rbVideo.Enabled = enabled;
            rbAudio.Enabled = enabled;
            cmbVideo.Enabled = enabled;
            cmbAudio.Enabled = enabled;
            cmbPlaylistQuality.Enabled = enabled;
            playlistEntries.Enabled = enabled;
        }

        void RenderPreview()
        {
            if (preview?.Playlist is YtPlaylistInfo pl)
            {
                playlist = pl;
                lblTitle.Text = pl.Title;
                lblSource.Text = SourceLine(pl.Extractor, pl.Uploader, 0) + Environment.NewLine +
                    "Playlist entries: " + pl.CountDescription + Environment.NewLine +
                    "Save folder: " + txtFolder.Text;
                singlePanel.Visible = false;
                playlistPanel.Visible = true;
                SetOptionsMode(isPlaylist: true, hasPreview: true);
                txtName.Enabled = false;
                cmbPlaylistQuality.Enabled = true;
                playlistEntries.Items.Clear();
                foreach (var entry in pl.Entries)
                {
                    bool check = entry.Available && playlistEntries.CheckedItems.Count < maxPlaylistEntries;
                    playlistEntries.Items.Add(new PlaylistItem { Entry = entry }, check);
                }
                bool hasFfmpeg = FfmpegLocator.Find() != null;
                if (!hasFfmpeg)
                {
                    for (int i = cmbPlaylistQuality.Items.Count - 1; i >= 0; i--)
                        if (cmbPlaylistQuality.Items[i] is BatchQuality quality && quality.AudioOnly)
                            cmbPlaylistQuality.Items.RemoveAt(i);
                    lblStatus.Text = "Playlist sizes are not known yet. FFmpeg is unavailable; audio conversion presets are hidden.";
                }
                else
                {
                    lblStatus.Text = "Playlist sizes are not known yet; each video's size depends on its selected format.";
                }
                UpdatePlaylistCount();
            }
            else if (preview?.Video is YtInfo video)
            {
                info = video;
                lblTitle.Text = video.Title;
                lblSource.Text = SourceLine(video.Extractor, video.Uploader, video.Duration) + Environment.NewLine +
                    "Available formats: " + video.Videos.Count + " video quality option(s)";
                singlePanel.Visible = true;
                playlistPanel.Visible = false;
                SetOptionsMode(isPlaylist: false, hasPreview: true);
                txtName.Enabled = true;
                txtName.Text = DefaultName(video.Title);
                FillVideoFormats(video);
                bool hasFfmpeg = FfmpegLocator.Find() != null;
                rbAudio.Enabled = hasFfmpeg && video.HasAudio;
                if (!rbAudio.Enabled) rbAudio.Checked = false;
                rbVideo.Enabled = cmbVideo.Items.Count > 0;
                if (!rbVideo.Enabled)
                {
                    rbVideo.Checked = false;
                    if (rbAudio.Enabled) rbAudio.Checked = true;
                }
                UpdateSingleMode();
            }
        }

        void FillVideoFormats(YtInfo video)
        {
            bool hasFfmpeg = FfmpegLocator.Find() != null;
            cmbVideo.Items.Clear();
            foreach (var option in video.Videos)
            {
                string? selector = option.GetSelector(hasFfmpeg);
                if (selector == null) continue;
                string label = option.Label;
                if (hasFfmpeg && option.RequiresMerge)
                    label += "  •  video + audio merged";
                else if (!hasFfmpeg)
                    label += "  •  single stream (no merge)";
                cmbVideo.Items.Add(new OptItem { Text = label, Tag = option });
            }
            if (cmbVideo.Items.Count > 0) cmbVideo.SelectedIndex = 0;
            if (!hasFfmpeg)
                lblMerge.Text = "FFmpeg is unavailable. Only formats that do not need audio/video merging are offered.";
        }

        void UpdateSingleMode()
        {
            bool audio = rbAudio.Checked;
            cmbVideo.Visible = !audio;
            cmbAudio.Visible = audio;
            UpdateSizeAndMerge();
        }

        void SetOptionsMode(bool isPlaylist, bool hasPreview)
        {
            lblPlaylistQuality.Visible = hasPreview && isPlaylist;
            cmbPlaylistQuality.Visible = hasPreview && isPlaylist;
            lblName.Visible = hasPreview && !isPlaylist;
            txtName.Visible = hasPreview && !isPlaylist;
            options.RowStyles[0].Height = hasPreview && isPlaylist ? Theme.Scale(this, 24) : 0;
            options.RowStyles[1].Height = hasPreview && isPlaylist ? Theme.Scale(this, 34) : 0;
            options.RowStyles[2].Height = hasPreview && !isPlaylist ? Theme.Scale(this, 24) : 0;
            options.RowStyles[3].Height = hasPreview && !isPlaylist ? Theme.Scale(this, 34) : 0;
            options.PerformLayout();
            UpdateComboDropdownWidths();
        }

        void UpdateComboDropdownWidths()
        {
            int available = Math.Max(120, ClientSize.Width - Theme.Scale(this, 32));
            foreach (var combo in new[] { cmbVideo, cmbAudio, cmbPlaylistQuality })
                combo.FitDropdownToDialog(available);
        }

        void UpdateSizeAndMerge()
        {
            if (info == null) return;
            if (rbAudio.Checked)
            {
                lblSize.Text = info.BestAudioBytes > 0
                    ? "Estimated source size: about " + YtDlpInfo.Fmt(info.BestAudioBytes)
                    : "Estimated size: unknown";
                lblMerge.Text = "Audio extraction uses FFmpeg. MP3 conversion re-encodes the source audio.";
                return;
            }
            var option = (cmbVideo.SelectedItem as OptItem)?.Tag as YtVideoOpt;
            var visibleOptions = cmbVideo.Items.Cast<OptItem>()
                .Select(x => x.Tag as YtVideoOpt)
                .Where(x => x != null)
                .Cast<YtVideoOpt>()
                .ToList();
            bool estimatesRepeat = visibleOptions.Count > 1 &&
                visibleOptions.All(x => x.EstBytes > 0) &&
                visibleOptions.Select(x => x.EstBytes).Distinct().Count() == 1;
            lblSize.Text = option != null && option.EstBytes > 0 && !estimatesRepeat
                ? "Estimated download size: about " + YtDlpInfo.Fmt(option.EstBytes)
                : "Estimated download size: unknown";
            lblMerge.Text = option?.RequiresMerge == true && FfmpegLocator.Find() != null
                ? "Video and audio will be joined after download."
                : FfmpegLocator.Find() == null
                    ? "FFmpeg is unavailable; this format does not require a merge."
                    : "One stream; no merging needed.";
        }

        void OnPlaylistItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (e.NewValue == CheckState.Checked && playlistEntries.Items[e.Index] is PlaylistItem item && !item.Entry.Available)
                e.NewValue = CheckState.Unchecked;
            else if (e.NewValue == CheckState.Checked && e.CurrentValue != CheckState.Checked && playlistEntries.CheckedItems.Count >= maxPlaylistEntries)
            {
                e.NewValue = CheckState.Unchecked;
                lblStatus.Text = "The playlist selection limit is " + maxPlaylistEntries + ". Change Maximum URLs in Preferences to select more.";
            }
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(UpdatePlaylistCount));
        }

        void UpdatePlaylistCount()
        {
            if (playlist == null) return;
            int selected = playlistEntries.CheckedItems.Cast<PlaylistItem>().Count(x => x.Entry.Available);
            lblCount.Text = playlist.CountDescription + "  •  " + selected + " selected (limit " + maxPlaylistEntries + ")";
        }

        void Accept(bool later)
        {
            if (loading || !ValidateOutput()) return;
            var result = new List<YtDlpSpec>();
            if (playlist != null)
            {
                var quality = cmbPlaylistQuality.SelectedItem as BatchQuality ?? BatchQuality.Presets[0];
                foreach (PlaylistItem item in playlistEntries.CheckedItems.Cast<PlaylistItem>().Take(maxPlaylistEntries))
                {
                    if (!item.Entry.Available || string.IsNullOrWhiteSpace(item.Entry.Url)) continue;
                    result.Add(new YtDlpSpec
                    {
                        Url = item.Entry.Url,
                        BaseName = DownloadEngine.Sanitize(item.Entry.Title),
                        Format = quality.Format,
                        AudioOnly = quality.AudioOnly,
                        AudioFormat = quality.AudioFormat,
                        IgnoreCookies = ignoreCookies,
                        NameFromTitle = true
                    });
                }
                if (result.Count == 0)
                {
                    ThemeDialog.Show(this, "Select at least one available video.", "Video playlist");
                    return;
                }
            }
            else if (info != null)
            {
                string baseName = DownloadEngine.Sanitize(txtName.Text.Trim()).TrimEnd('.', ' ');
                var spec = new YtDlpSpec { Url = url, BaseName = baseName, IgnoreCookies = ignoreCookies };
                if (preview?.InfoJsonPath is string path && preview.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    spec.InfoJsonPath = path;
                    spec.InfoJsonExpiresAt = preview.ExpiresAt;
                }
                if (rbAudio.Checked)
                {
                    spec.AudioOnly = true;
                    spec.Format = "ba/b";
                    spec.AudioFormat = (cmbAudio.SelectedItem as OptItem)?.Tag as string ?? "m4a";
                    spec.EstimatedBytes = info.BestAudioBytes;
                }
                else
                {
                    var option = (cmbVideo.SelectedItem as OptItem)?.Tag as YtVideoOpt;
                    spec.Format = option?.GetSelector(FfmpegLocator.Find() != null) ?? "bv*+ba/b";
                    spec.EstimatedBytes = option?.EstBytes ?? 0;
                }
                result.Add(spec);
            }

            Specs = result;
            DialogResult = later ? DialogResult.Yes : DialogResult.OK;
            Close();
        }

        bool ValidateOutput()
        {
            if (string.IsNullOrWhiteSpace(txtFolder.Text))
            {
                ThemeDialog.Show(this, "Choose a folder for the download.", "Video");
                return false;
            }
            if (playlist == null && string.IsNullOrWhiteSpace(txtName.Text))
            {
                ThemeDialog.Show(this, "Enter a file name.", "Video");
                txtName.Focus();
                return false;
            }
            return true;
        }

        async Task LoadThumbnailAsync(string imageUrl, CancellationToken ct, bool requiresConversion = false)
        {
            string host = ThumbnailHost(imageUrl);
            System.Net.HttpStatusCode? status = null;
            string reason = "unknown";
            bool webp = requiresConversion || YtThumbnailPolicy.IsWebp(imageUrl);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(YtThumbnailPolicy.Timeout);
                byte[]? bytes = null;
                if (!webp)
                {
                    var fetched = await FetchThumbnailAsync(imageUrl, timeout.Token);
                    host = fetched.Host;
                    status = fetched.Status;
                    reason = fetched.Reason;
                    bytes = fetched.Bytes;
                    webp |= fetched.IsWebp;
                    if (bytes != null && TryDisplayThumbnail(bytes, out reason)) return;
                }
                else reason = "webp_requires_yt_dlp_conversion";

                if (webp)
                {
                    var converted = await ConvertThumbnailWithYtDlpAsync(timeout.Token);
                    if (converted.Bytes != null && TryDisplayThumbnail(converted.Bytes, out reason)) return;
                    if (converted.Reason.Length > 0) reason = converted.Reason;
                }
            }
            catch (OperationCanceledException)
            {
                reason = ct.IsCancellationRequested ? "cancelled" : "timeout_15s";
            }
            catch (Exception ex)
            {
                reason = "thumbnail_error: " + AppLog.Redact(ex.GetBaseException().Message);
            }

            ShowThumbnailPlaceholder();
            LogThumbnailFailure(host, status, reason);
        }

        static string ThumbnailHost(string imageUrl) =>
            YtThumbnailPolicy.TryParseHttpsUri(imageUrl, out var uri, out _) ? uri.DnsSafeHost : "invalid";

        static void LogThumbnailFailure(string host, System.Net.HttpStatusCode? status, string reason) =>
            AppLog.Write(AppLog.Redact("Video preview thumbnail unavailable (host=" + host + ", status=" +
                (status.HasValue ? ((int)status.Value).ToString() : "none") + ", reason=" + reason + ")"));

        async Task<ThumbnailFetchResult> FetchThumbnailAsync(string imageUrl, CancellationToken cancellationToken)
        {
            var result = new ThumbnailFetchResult { Host = ThumbnailHost(imageUrl), Reason = "unknown" };
            if (!YtThumbnailPolicy.TryParseHttpsUri(imageUrl, out var start, out var urlReason))
            {
                result.Reason = urlReason;
                return result;
            }

            using var client = CreateThumbnailClient();
            for (int attempt = 0; attempt < YtThumbnailPolicy.MaxFetchAttempts; attempt++)
            {
                Uri current = start;
                int redirects = 0;
                try
                {
                    while (true)
                    {
                        result.Host = current.DnsSafeHost;
                        var check = await YtThumbnailPolicy.CheckHostAsync(current,
                            (name, token) => System.Net.Dns.GetHostAddressesAsync(name, token), cancellationToken);
                        if (!check.Allowed)
                        {
                            result.Reason = check.Reason;
                            return result;
                        }

                        using var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        result.Status = response.StatusCode;
                        if (IsRedirect(response.StatusCode))
                        {
                            string? location = response.Headers.Location?.OriginalString;
                            if (!YtThumbnailPolicy.TryResolveRedirect(current, location, redirects, out var next, out var redirectReason))
                            {
                                result.Reason = redirectReason;
                                return result;
                            }
                            current = next;
                            redirects++;
                            continue;
                        }
                        if (!response.IsSuccessStatusCode)
                        {
                            result.Reason = "http_status_" + (int)response.StatusCode;
                            break;
                        }

                        string? mediaType = response.Content.Headers.ContentType?.MediaType;
                        if (mediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
                        {
                            result.Reason = "content_type_not_image";
                            break;
                        }
                        result.IsWebp = string.Equals(mediaType, "image/webp", StringComparison.OrdinalIgnoreCase);
                        if (result.IsWebp)
                        {
                            result.Reason = "webp_requires_yt_dlp_conversion";
                            return result;
                        }
                        if (response.Content.Headers.ContentLength > YtThumbnailPolicy.MaxImageBytes)
                        {
                            result.Reason = "content_length_over_2mb";
                            return result;
                        }

                        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        using var memory = new MemoryStream();
                        var buffer = new byte[8192];
                        int total = 0;
                        while (true)
                        {
                            int count = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                            if (count == 0) break;
                            total += count;
                            if (total > YtThumbnailPolicy.MaxImageBytes)
                            {
                                result.Reason = "body_over_2mb";
                                return result;
                            }
                            memory.Write(buffer, 0, count);
                        }
                        result.Bytes = memory.ToArray();
                        result.Reason = "";
                        return result;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) { result.Reason = "timeout_15s"; }
                catch (HttpRequestException ex) { result.Reason = "network_error: " + AppLog.Redact(ex.GetBaseException().Message); }
                if (attempt + 1 < YtThumbnailPolicy.MaxFetchAttempts) continue;
            }
            return result;
        }

        static HttpClient CreateThumbnailClient()
        {
            var mode = ProxyConfig.Mode;
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = YtThumbnailPolicy.Timeout,
                UseProxy = mode != ProxyMode.None,
                Proxy = mode == ProxyMode.None ? null : DynamicProxy.Instance
            };
            return new HttpClient(handler)
            {
                Timeout = YtThumbnailPolicy.Timeout,
                MaxResponseContentBufferSize = YtThumbnailPolicy.MaxImageBytes
            };
        }

        bool TryDisplayThumbnail(byte[] bytes, out string reason)
        {
            try
            {
                using var memory = new MemoryStream(bytes, writable: false);
                using var image = Image.FromStream(memory);
                if (!YtThumbnailPolicy.IsSafeImageDimensions(image.Width, image.Height))
                {
                    reason = "image_dimensions_over_limit";
                    return false;
                }
                var copy = new Bitmap(image);
                if (IsDisposed || Disposing) { copy.Dispose(); reason = "form_closed"; return false; }
                var old = thumbnail.Image;
                thumbnail.Image = copy;
                old?.Dispose();
                thumbnailSkeleton.Visible = false;
                thumbnailPlaceholder.Visible = false;
                reason = "";
                return true;
            }
            catch (Exception ex)
            {
                reason = "image_decode_failed: " + AppLog.Redact(ex.GetBaseException().Message);
                return false;
            }
        }

        async Task<(byte[]? Bytes, string Reason)> ConvertThumbnailWithYtDlpAsync(CancellationToken ct)
        {
            string? exe = YtDlpLocator.Find();
            if (exe == null) return (null, "yt_dlp_unavailable_for_webp_conversion");
            string folder = Path.Combine(Path.GetTempPath(), "FastDM-thumbnail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var diagnostic = new StringBuilder();
            var diagnosticLock = new object();
            try
            {
                var args = YtDlpRunner.CommonArgs(ignoreCookies);
                args.AddRange(new[] { "--skip-download", "--write-thumbnail", "--convert-thumbnails", "jpg", "--no-part", "-o", Path.Combine(folder, "%(id)s.%(ext)s") });
                var input = YtDlpUrlInput.Create(url);
                args.AddRange(input.Arguments);
                int exitCode = await YtDlpRunner.RunAsync(exe, args,
                    line => { lock (diagnosticLock) if (diagnostic.Length < 8000) diagnostic.AppendLine(line); },
                    line => { lock (diagnosticLock) if (diagnostic.Length < 8000) diagnostic.AppendLine(line); },
                    ct, input.StandardInput, maxOutputLineCharacters: 4096);
                string details;
                lock (diagnosticLock) details = AppLog.Redact(diagnostic.ToString().Trim());
                if (exitCode != 0)
                    return (null, "yt_dlp_conversion_exit_" + exitCode + (details.Length == 0 ? "" : ": " + details));

                string? file = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase));
                if (file == null) return (null, "yt_dlp_converted_thumbnail_missing" + (details.Length == 0 ? "" : ": " + details));
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > YtThumbnailPolicy.MaxImageBytes) return (null, "converted_thumbnail_over_2mb");
                return (await File.ReadAllBytesAsync(file, ct), "");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return (null, "yt_dlp_conversion_failed: " + AppLog.Redact(ex.GetBaseException().Message)); }
            finally
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception ex) { AppLog.Write("Temporary thumbnail cleanup failed: " + AppLog.Redact(ex.Message)); }
            }
        }

        void ShowThumbnailPlaceholder()
        {
            if (IsDisposed || Disposing) return;
            thumbnailSkeleton.Visible = false;
            if (thumbnailPlaceholder.Text.Length == 0) thumbnailPlaceholder.Text = ThumbnailInitial();
            thumbnailPlaceholder.Visible = true;
            thumbnailPlaceholder.BringToFront();
        }

        string ThumbnailInitial()
        {
            try
            {
                string host = new Uri(url).DnsSafeHost;
                if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host.Substring(4);
                return host.Length == 0 ? "▶" : host.Substring(0, 1).ToUpperInvariant();
            }
            catch { return "▶"; }
        }

        static bool IsRedirect(System.Net.HttpStatusCode status) =>
            status is System.Net.HttpStatusCode.Moved or System.Net.HttpStatusCode.Redirect or
                System.Net.HttpStatusCode.RedirectMethod or System.Net.HttpStatusCode.TemporaryRedirect or
                System.Net.HttpStatusCode.PermanentRedirect;

        string SourceLine(string? extractor, string? uploader, double duration)
        {
            string result = "Source: " + (extractor ?? new Uri(url).Host);
            if (!string.IsNullOrWhiteSpace(uploader)) result += "  •  Channel: " + uploader;
            if (duration > 0)
            {
                var time = TimeSpan.FromSeconds(duration);
                result += "  •  Duration: " + (time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss"));
            }
            return result;
        }

        static string DefaultName(string title)
        {
            string name = DownloadEngine.Sanitize(title ?? "").TrimEnd('.', ' ');
            if (name.Length > 120) name = name.Substring(0, 120).TrimEnd('.', ' ');
            return name.Length == 0 ? "video" : name;
        }
    }
}
