using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FastDM
{
    // ====================== কোয়ালিটি / অডিও বাছাই ডায়ালগ ======================
    class MediaPickerForm : Form
    {
        class OptItem
        {
            public string Text = "";
            public object? Tag;
            public override string ToString() => Text;
        }

        readonly MediaOptions o;
        readonly ComboBox cmbVideo, cmbAudio;
        readonly CheckBox chkAudioOnly;
        readonly TextBox txtName, txtFolder;

        public StreamSpec? Spec { get; private set; }
        public string FileName => txtName.Text.Trim();
        public string SaveFolder => txtFolder.Text.Trim();

        public MediaPickerForm(MediaOptions options, string defaultFolder)
        {
            Theme.ConfigureForm(this);
            o = options;
            Text = (o.Kind == "hls" ? "HLS" : "DASH") + " stream — choose quality";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(600, 500);
            MinimumSize = new Size(500, 430);
            Font = Theme.CreateFont(Theme.FontBody);

            var fields = DialogLayout.CreateTwoColumnFields(this, labelWidth: 120);

            cmbVideo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Video quality" };
            foreach (var v in o.Videos) cmbVideo.Items.Add(new OptItem { Text = v.Label, Tag = v });
            if (cmbVideo.Items.Count > 0) cmbVideo.SelectedIndex = 0;
            cmbVideo.SelectedIndexChanged += (a, b) => RefreshAudio();
            DialogLayout.AddFieldRow(fields, "Video quality", cmbVideo);

            cmbAudio = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Audio track" };
            DialogLayout.AddFieldRow(fields, "Audio track", cmbAudio);

            chkAudioOnly = new CheckBox { Text = "Audio only (save as .m4a)", AutoSize = true };
            chkAudioOnly.CheckedChanged += (a, b) => cmbVideo.Enabled = !chkAudioOnly.Checked && o.Videos.Count > 0;
            DialogLayout.AddFieldRow(fields, "", chkAudioOnly);

            txtName = new TextBox { Text = DefaultName(), AccessibleName = "File name" };
            DialogLayout.AddFieldRow(fields, "File name", txtName);

            txtFolder = new TextBox { Text = defaultFolder, AccessibleName = "Save folder", Dock = DockStyle.Fill };
            var folderRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                ColumnCount = 2,
                RowCount = 1
            };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Scale(this, 120)));
            var browse = DialogLayout.CreateButton(this, "Browse…");
            browse.Dock = DockStyle.Fill;
            browse.Margin = Padding.Empty;
            browse.Click += (a, b) =>
            {
                using var fb = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (fb.ShowDialog(this) == DialogResult.OK) txtFolder.Text = fb.SelectedPath;
            };
            folderRow.Controls.Add(txtFolder, 0, 0);
            folderRow.Controls.Add(browse, 1, 0);
            DialogLayout.AddFieldRow(fields, "Save to", folderRow);

            bool hasFfmpeg = FfmpegLocator.Find() != null;
            var ffmpegNote = new Label
            {
                Text = hasFfmpeg
                    ? "ffmpeg found: the result will be one MP4 file (streams are copied, nothing is re-encoded)."
                    : "ffmpeg was not found: audio and video will be saved as separate files.",
                AutoSize = true,
                MaximumSize = new Size(Theme.Scale(this, 440), 0),
                ForeColor = Theme.P.SubText
            };
            DialogLayout.AddFullRow(fields, ffmpegNote);

            var drmNote = new Label
            {
                Text = "DRM-protected streams cannot be downloaded.",
                AutoSize = true,
                MaximumSize = new Size(Theme.Scale(this, 440), 0),
                ForeColor = Theme.P.SubText
            };
            DialogLayout.AddFullRow(fields, drmNote);

            var ok = DialogLayout.CreateButton(this, "Download");
            var cancel = DialogLayout.CreateButton(this, "Cancel", DialogResult.Cancel);
            var footer = DialogLayout.CreateFooter(this, cancel, ok);
            Controls.Add(DialogLayout.CreateShell(this, fields, footer));
            ok.Click += OnOk;
            AcceptButton = ok;
            CancelButton = cancel;

            if (o.Videos.Count == 0)
            {
                cmbVideo.Enabled = false;
                chkAudioOnly.Checked = true;
                chkAudioOnly.Enabled = false;
            }
            RefreshAudio();
            Shown += (a, b) =>
            {
                if (cmbVideo.Enabled) cmbVideo.Focus();
                else txtName.Focus();
            };
            Resize += (a, b) =>
            {
                int width = Math.Max(Theme.Scale(this, 160), fields.ClientSize.Width - Theme.Scale(this, 150));
                ffmpegNote.MaximumSize = new Size(width, 0);
                drmNote.MaximumSize = new Size(width, 0);
            };
        }

        string DefaultName()
        {
            string? n = o.Title;
            if (string.IsNullOrWhiteSpace(n))
            {
                try
                {
                    n = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(new Uri(o.ManifestUrl).AbsolutePath));
                }
                catch { n = ""; }
            }
            n = DownloadEngine.Sanitize(n ?? "").TrimEnd('.', ' ');
            if (n.Length > 120) n = n.Substring(0, 120);
            return n.Length == 0 ? "video" : n;
        }

        void RefreshAudio()
        {
            cmbAudio.Items.Clear();
            var v = (cmbVideo.SelectedItem as OptItem)?.Tag as VideoOpt;

            IEnumerable<AudioOpt> list;
            if (o.Kind == "hls")
                list = v != null && v.AudioGroup != null ? o.Audios.Where(a => a.Group == v.AudioGroup) : Enumerable.Empty<AudioOpt>();
            else
                list = o.Audios;

            foreach (var a in list) cmbAudio.Items.Add(new OptItem { Text = a.Label, Tag = a });

            if (cmbAudio.Items.Count == 0)
            {
                cmbAudio.Items.Add(new OptItem { Text = "(audio is included in the video)", Tag = null });
            }
            else if (o.Kind == "dash" && o.Videos.Count > 0)
            {
                cmbAudio.Items.Add(new OptItem { Text = "None (video only)", Tag = null });
            }
            cmbAudio.SelectedIndex = 0;
            cmbAudio.Enabled = cmbAudio.Items.Count > 1;
        }

        void OnOk(object? sender, EventArgs e)
        {
            if (txtName.Text.Trim().Length == 0) { ThemeDialog.Show(this, "Please enter a file name."); return; }
            if (txtFolder.Text.Trim().Length == 0) { ThemeDialog.Show(this, "Please choose a folder."); return; }

            var v = (cmbVideo.SelectedItem as OptItem)?.Tag as VideoOpt;
            var a = (cmbAudio.SelectedItem as OptItem)?.Tag as AudioOpt;
            bool audioOnly = chkAudioOnly.Checked;

            var spec = new StreamSpec
            {
                Kind = o.Kind,
                ManifestUrl = o.ManifestUrl,
                Referer = o.Referer,
                AudioOnly = audioOnly
            };

            if (o.Kind == "hls")
            {
                // শুধু অডিও চাই কিন্তু অডিও ভিডিওর ভেতরেই: সবচেয়ে কম বিটরেটের ভিডিও নামিয়ে অডিও বের করা হবে
                if (audioOnly && a == null)
                    v = o.Videos.OrderBy(x => x.Bandwidth <= 0 ? long.MaxValue : x.Bandwidth).FirstOrDefault() ?? v;
                spec.VideoUrl = v?.Url;
                spec.AudioUrl = a?.Url;
            }
            else
            {
                if (audioOnly && a == null)
                {
                    ThemeDialog.Show(this, "This stream has no separate audio track to save.");
                    return;
                }
                spec.VideoRepId = v?.RepId;
                spec.AudioRepId = a?.RepId;
            }

            Spec = spec;
            DialogResult = DialogResult.OK;
        }
    }

    // ====================== ওয়েবপেজে পাওয়া ভিডিও লিঙ্ক থেকে বাছাই ======================
    class CandidateForm : Form
    {
        readonly ListBox list;
        public MediaCandidate? Picked { get; private set; }

        public CandidateForm(List<MediaCandidate> candidates)
        {
            Theme.ConfigureForm(this);
            Text = "Videos found on this page";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(660, 420);
            MinimumSize = new Size(500, 320);
            Font = Theme.CreateFont(Theme.FontBody);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(Theme.Scale(this, DialogLayout.StandardMargin)),
                Margin = Padding.Empty,
                ColumnCount = 1,
                RowCount = 2
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(new Label
            {
                Text = "Several videos were found. Choose the one to download:",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Theme.Scale(this, 10))
            }, 0, 0);

            list = new ListBox
            {
                Dock = DockStyle.Fill,
                HorizontalScrollbar = true,
                AccessibleName = "Videos found on this page",
                AccessibleDescription = "Select a video, then choose Next. Double-click a video to continue."
            };
            foreach (var candidate in candidates) list.Items.Add(candidate);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            list.DoubleClick += (a, b) => Accept();
            body.Controls.Add(list, 0, 1);

            var next = DialogLayout.CreateButton(this, "Next");
            var cancel = DialogLayout.CreateButton(this, "Cancel", DialogResult.Cancel);
            var footer = DialogLayout.CreateFooter(this, cancel, next);
            Controls.Add(DialogLayout.CreateShell(this, body, footer));
            next.Click += (a, b) => Accept();
            AcceptButton = next;
            CancelButton = cancel;
            Shown += (a, b) => list.Focus();
        }

        void Accept()
        {
            if (list.SelectedItem is MediaCandidate c)
            {
                Picked = c;
                DialogResult = DialogResult.OK;
            }
        }
    }
}
