using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace FastDM
{
    // ====================== শিডিউল ডায়ালগ ======================
    class ScheduleForm : Form
    {
        readonly AppSettings s;
        readonly CheckBox chkEnable, chkEvery, chkStop;
        readonly CheckBox[] days = new CheckBox[7];
        readonly ComboBox cmbFrom, cmbTo;
        bool updating;

        static readonly string[] DayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        public ScheduleForm(AppSettings settings)
        {
            Theme.ConfigureForm(this);
            s = settings;
            Text = "Scheduler";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(480, 340);
            MinimumSize = new Size(460, 320);
            Font = new Font("Segoe UI", 9.5f);

            chkEnable = new CheckBox
            {
                Text = "Enable scheduler (download only during the time window below)",
                Location = new Point(16, 16),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Checked = s.SchedulerEnabled
            };
            Controls.Add(chkEnable);

            Controls.Add(new Label { Text = "Days", Location = new Point(16, 56), AutoSize = true });

            chkEvery = new CheckBox { Text = "Everyday", Location = new Point(16, 82), AutoSize = true };
            Controls.Add(chkEvery);

            for (int i = 0; i < 7; i++)
            {
                days[i] = new CheckBox
                {
                    Text = DayNames[i],
                    Location = new Point(16 + i * 62, 114),
                    AutoSize = true,
                    Checked = (s.ScheduleDays & (1 << i)) != 0
                };
                int idx = i;
                days[i].CheckedChanged += (a, b) => { if (!updating) SyncEveryday(); };
                Controls.Add(days[i]);
            }
            SyncEveryday();
            chkEvery.CheckedChanged += (a, b) =>
            {
                if (updating) return;
                updating = true;
                foreach (var d in days) d.Checked = chkEvery.Checked;
                updating = false;
            };

            Controls.Add(new Label { Text = "From", Location = new Point(16, 164), AutoSize = true });
            cmbFrom = MakeTimeCombo(new Point(64, 160), s.ScheduleStartMin);
            Controls.Add(cmbFrom);

            Controls.Add(new Label { Text = "To", Location = new Point(200, 164), AutoSize = true });
            cmbTo = MakeTimeCombo(new Point(232, 160), s.ScheduleEndMin);
            Controls.Add(cmbTo);

            chkStop = new CheckBox
            {
                Text = "Pause running downloads when the window ends",
                Location = new Point(16, 204),
                AutoSize = true,
                Checked = s.ScheduleStopOutside
            };
            Controls.Add(chkStop);

            Controls.Add(new Label
            {
                Text = "A window that crosses midnight works too (for example 22:00 to 06:00).\n" +
                       "Right-click a download and choose \"Start now\" to ignore the schedule for it.",
                Location = new Point(16, 238),
                Size = new Size(450, 44),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });

            var ok = new Button { Text = "OK", Location = new Point(290, 292), Size = new Size(90, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            var cancel = new Button { Text = "Cancel", Location = new Point(386, 292), Size = new Size(84, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }

        void SyncEveryday()
        {
            updating = true;
            bool all = true;
            foreach (var d in days) if (!d.Checked) all = false;
            chkEvery.Checked = all;
            updating = false;
        }

        static ComboBox MakeTimeCombo(Point loc, int minutes)
        {
            var c = new ComboBox { Location = loc, Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            for (int m = 0; m < 1440; m += 15) c.Items.Add(Scheduler.Hm(m));
            c.SelectedIndex = Math.Clamp((int)Math.Round(minutes / 15.0), 0, 95);
            return c;
        }

        void OnOk(object? sender, EventArgs e)
        {
            int mask = 0;
            for (int i = 0; i < 7; i++) if (days[i].Checked) mask |= 1 << i;

            if (chkEnable.Checked && mask == 0)
            {
                MessageBox.Show(this, "Select at least one day.");
                return;
            }

            s.SchedulerEnabled = chkEnable.Checked;
            s.ScheduleDays = mask == 0 ? 0x7F : mask;
            s.ScheduleStartMin = cmbFrom.SelectedIndex * 15;
            s.ScheduleEndMin = cmbTo.SelectedIndex * 15;
            s.ScheduleStopOutside = chkStop.Checked;
            DialogResult = DialogResult.OK;
        }
    }

    // ====================== কাউন্টডাউন ডায়ালগ (শাটডাউন/স্লিপ/হাইবারনেটের আগে) ======================
    class CountdownForm : Form
    {
        readonly PostAction act;
        readonly Label lbl;
        readonly System.Windows.Forms.Timer timer;
        int left;

        public CountdownForm(PostAction action, int seconds)
        {
            Theme.ConfigureForm(this);
            act = action;
            left = seconds;

            Text = "FastDM — all downloads finished";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            ClientSize = new Size(430, 150);
            Font = new Font("Segoe UI", 10f);

            lbl = new Label { Location = new Point(20, 20), Size = new Size(390, 60) };
            Controls.Add(lbl);

            var now = new Button { Text = "Do it now", Location = new Point(210, 96), Size = new Size(110, 36) };
            var cancel = new Button { Text = "Cancel", Location = new Point(326, 96), Size = new Size(84, 36), DialogResult = DialogResult.Cancel };
            now.Click += (a, b) => DialogResult = DialogResult.OK;
            Controls.Add(now); Controls.Add(cancel);
            AcceptButton = cancel; CancelButton = cancel;     // Enter চাপলে ভুলে শাটডাউন না হয়ে বাতিল হবে

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (a, b) =>
            {
                left--;
                if (left <= 0) { timer.Stop(); DialogResult = DialogResult.OK; return; }
                UpdateText();
            };
            Shown += (a, b) => { UpdateText(); timer.Start(); };
            FormClosed += (a, b) => timer.Dispose();
        }

        void UpdateText()
        {
            lbl.Text = "All downloads are finished.\nYour computer will " + PowerActions.Name(act) +
                       " in " + left + " second(s).";
        }
    }
}
