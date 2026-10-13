using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FastDM
{
    class YtPickerForm : Form
    {
        class OptItem
        {
            public string Text = string.Empty;
            public object? Tag;
            public override string ToString() => Text;
        }

        readonly YtInfo info;
        readonly string url;
        readonly RadioButton rbVideo, rbAudio;
        readonly ComboBox cmbVideo, cmbAudio;
        readonly Label lblSize;
        readonly TextBox txtName, txtFolder;

        public YtDlpSpec Spec { get; private set; } = null!;
        public string SaveFolder => txtFolder.Text.Trim();

        public YtPickerForm(YtInfo videoInfo, string videoUrl, string defaultFolder)
        {
            info = videoInfo;
            url = videoUrl;

            Text = "Download video";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(580, 480);
            Font = new Font("Segoe UI", 9.5f);

            // শিরোনাম + তথ্য
            Controls.Add(new Label
            {
                Text = info.Title,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Location = new Point(16, 12),
                Size = new Size(548, 44),
                AutoEllipsis = true
            });

            string sub = "";
            if (!string.IsNullOrEmpty(info.Extractor)) sub += info.Extractor;
            if (!string.IsNullOrEmpty(info.Uploader)) sub += (sub.Length > 0 ? "  •  " : "") + info.Uploader;
            if (info.Duration > 0)
            {
                var t = TimeSpan.FromSeconds(info.Duration);
                string dur = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
                sub += (sub.Length > 0 ? "  •  " : "") + dur;
            }
            Controls.Add(new Label { Text = sub, Location = new Point(16, 58), Size = new Size(548, 20) });

            // ভিডিও / অডিও মোড
            rbVideo = new RadioButton { Text = "Video + audio (MP4)", Location = new Point(16, 92), AutoSize = true, Checked = true };
            rbAudio = new RadioButton { Text = "Audio only", Location = new Point(220, 92), AutoSize = true };
            Controls.Add(rbVideo);
            Controls.Add(rbAudio);

            Controls.Add(new Label { Text = "Quality", Location = new Point(16, 128), AutoSize = true });
            cmbVideo = new ComboBox { Location = new Point(16, 150), Width = 548, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var v in info.Videos) cmbVideo.Items.Add(new OptItem { Text = v.Label, Tag = v });
            if (cmbVideo.Items.Count > 0) cmbVideo.SelectedIndex = 0;
            cmbVideo.SelectedIndexChanged += (a, b) => UpdateSize();
            Controls.Add(cmbVideo);

            cmbAudio = new ComboBox { Location = new Point(16, 150), Width = 548, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
            cmbAudio.Items.Add(new OptItem { Text = "M4A  •  best audio (no re-encode when the source is AAC)", Tag = "m4a" });
            cmbAudio.Items.Add(new OptItem { Text = "MP3  •  converted (needs MP3 support in ffmpeg)", Tag = "mp3" });
            cmbAudio.SelectedIndex = 0;
            Controls.Add(cmbAudio);

            lblSize = new Label { Location = new Point(16, 184), Size = new Size(548, 20) };
            Controls.Add(lblSize);

            rbAudio.CheckedChanged += (a, b) => ApplyMode();

            // নাম ও ফোল্ডার
            Controls.Add(new Label { Text = "File name", Location = new Point(16, 218), AutoSize = true });
            txtName = new TextBox { Location = new Point(16, 240), Width = 548, Text = DefaultName() };
            Controls.Add(txtName);

            Controls.Add(new Label { Text = "Save to", Location = new Point(16, 276), AutoSize = true });
            txtFolder = new TextBox { Location = new Point(16, 298), Width = 456, Text = defaultFolder };
            Controls.Add(txtFolder);
            var browse = new Button { Text = "Browse…", Location = new Point(480, 297), Size = new Size(84, 28) };
            browse.Click += (a, b) =>
            {
                using var fb = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (fb.ShowDialog(this) == DialogResult.OK) txtFolder.Text = fb.SelectedPath;
            };
            Controls.Add(browse);

            // টুল না থাকলে সতর্কতা
            string warn = "";
            if (FfmpegLocator.Find() == null)
                warn += "ffmpeg was not found: merging video+audio and audio conversion will not work.\n";
            if (!YtDlpLocator.HasJsRuntime())
                warn += "No JavaScript runtime (QuickJS or Deno) was found: some YouTube videos may fail to download.\n";
            Controls.Add(new Label { Text = warn, Location = new Point(16, 340), Size = new Size(548, 52) });

            var ok = new Button { Text = "Download", Location = new Point(344, 428), Size = new Size(110, 36) };
            var cancel = new Button { Text = "Cancel", Location = new Point(460, 428), Size = new Size(104, 36), DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            if (info.Videos.Count == 0)
            {
                rbVideo.Enabled = false;
                rbAudio.Checked = true;
            }
            ApplyMode();
            UpdateSize();
        }

        string DefaultName()
        {
            string n = Engine.Sanitize(info.Title ?? "").TrimEnd('.', ' ');
            if (n.Length > 120) n = n.Substring(0, 120).TrimEnd('.', ' ');
            return n.Length == 0 ? "video" : n;
        }

        void ApplyMode()
        {
            bool audio = rbAudio.Checked;
            cmbVideo.Visible = !audio;
            cmbAudio.Visible = audio;
            UpdateSize();
        }

        void UpdateSize()
        {
            if (rbAudio.Checked)
            {
                lblSize.Text = info.BestAudioBytes > 0 ? "Estimated source size: ≈ " + YtDlpInfo.Fmt(info.BestAudioBytes) : "";
                return;
            }
            var v = (cmbVideo.SelectedItem as OptItem)?.Tag as YtVideoOpt;
            lblSize.Text = v != null && v.EstBytes > 0 ? "Estimated size: ≈ " + YtDlpInfo.Fmt(v.EstBytes) : "";
        }

        void OnOk(object? sender, EventArgs e)
        {
            string name = Engine.Sanitize(txtName.Text.Trim()).TrimEnd('.', ' ');
            if (name.Length == 0) { MessageBox.Show(this, "Please enter a file name."); return; }
            if (txtFolder.Text.Trim().Length == 0) { MessageBox.Show(this, "Please choose a folder."); return; }

            var spec = new YtDlpSpec { Url = url, BaseName = name };

            if (rbAudio.Checked)
            {
                spec.AudioOnly = true;
                spec.Format = "ba/b";
                spec.AudioFormat = (cmbAudio.SelectedItem as OptItem)?.Tag as string ?? "m4a";
                spec.EstimatedBytes = info.BestAudioBytes;
            }
            else
            {
                var v = (cmbVideo.SelectedItem as OptItem)?.Tag as YtVideoOpt;
                spec.Format = v?.Selector ?? "bv*+ba/b";
                spec.EstimatedBytes = v?.EstBytes ?? 0;
            }

            Spec = spec;
            DialogResult = DialogResult.OK;
        }
    }
}
