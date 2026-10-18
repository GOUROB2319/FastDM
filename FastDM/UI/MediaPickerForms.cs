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
            MaximizeBox = true; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(560, 440);
            MinimumSize = new Size(480, 400);
            Font = Theme.CreateFont(Theme.FontBody);

            Controls.Add(new Label { Text = "Video quality", Location = new Point(16, 14), AutoSize = true });
            cmbVideo = new ComboBox { Location = new Point(16, 36), Width = 528, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var v in o.Videos) cmbVideo.Items.Add(new OptItem { Text = v.Label, Tag = v });
            if (cmbVideo.Items.Count > 0) cmbVideo.SelectedIndex = 0;
            cmbVideo.SelectedIndexChanged += (a, b) => RefreshAudio();
            Controls.Add(cmbVideo);

            Controls.Add(new Label { Text = "Audio track", Location = new Point(16, 74), AutoSize = true });
            cmbAudio = new ComboBox { Location = new Point(16, 96), Width = 528, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(cmbAudio);

            chkAudioOnly = new CheckBox { Text = "Audio only (save as .m4a)", Location = new Point(16, 134), AutoSize = true };
            chkAudioOnly.CheckedChanged += (a, b) => cmbVideo.Enabled = !chkAudioOnly.Checked && o.Videos.Count > 0;
            Controls.Add(chkAudioOnly);

            Controls.Add(new Label { Text = "File name", Location = new Point(16, 172), AutoSize = true });
            txtName = new TextBox { Location = new Point(16, 194), Width = 528, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = DefaultName() };
            Controls.Add(txtName);

            Controls.Add(new Label { Text = "Save to", Location = new Point(16, 230), AutoSize = true });
            txtFolder = new TextBox { Location = new Point(16, 252), Width = 436, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = defaultFolder };
            Controls.Add(txtFolder);
            var browse = new Button { Text = "Browse…", Location = new Point(460, 251), Size = new Size(84, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            browse.Click += (a, b) =>
            {
                using var fb = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (fb.ShowDialog(this) == DialogResult.OK) txtFolder.Text = fb.SelectedPath;
            };
            Controls.Add(browse);

            bool hasFfmpeg = FfmpegLocator.Find() != null;
            Controls.Add(new Label
            {
                Text = hasFfmpeg
                    ? "ffmpeg found: the result will be one MP4 file (streams are copied, nothing is re-encoded)."
                    : "ffmpeg was not found: audio and video will be saved as separate files.",
                Location = new Point(16, 296),
                Size = new Size(528, 44),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });

            Controls.Add(new Label
            {
                Text = "DRM-protected streams cannot be downloaded.",
                Location = new Point(16, 344),
                Size = new Size(528, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });

            var ok = new Button { Text = "Download", Location = new Point(330, 388), Size = new Size(110, 36), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            var cancel = new Button { Text = "Cancel", Location = new Point(446, 388), Size = new Size(98, 36), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            if (o.Videos.Count == 0)
            {
                cmbVideo.Enabled = false;
                chkAudioOnly.Checked = true;
                chkAudioOnly.Enabled = false;
            }
            RefreshAudio();
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
