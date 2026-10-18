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
        bool dirty;

        static readonly string[] DayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        public ScheduleForm(AppSettings settings)
        {
            Theme.ConfigureForm(this);
            s = settings;
            Text = "Scheduler";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 460);
            MinimumSize = new Size(460, 360);
            Font = Theme.CreateFont(Theme.FontBody);

            var fields = DialogLayout.CreateTwoColumnFields(this, labelWidth: 120);

            chkEnable = new CheckBox
            {
                Text = "Enable scheduler (download only during the time window below)",
                AutoSize = true,
                Checked = s.SchedulerEnabled,
                Margin = new Padding(0, Theme.Scale(this, 4), 0, Theme.Scale(this, 6))
            };
            DialogLayout.AddFullRow(fields, chkEnable);

            var dayPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = Padding.Empty
            };
            chkEvery = new CheckBox { Text = "Everyday", AutoSize = true, Margin = new Padding(0, 3, Theme.Scale(this, 14), 3) };
            dayPanel.Controls.Add(chkEvery);
            for (int i = 0; i < 7; i++)
            {
                days[i] = new CheckBox
                {
                    Text = DayNames[i],
                    AutoSize = true,
                    Checked = (s.ScheduleDays & (1 << i)) != 0,
                    Margin = new Padding(0, 3, Theme.Scale(this, 8), 3)
                };
                int idx = i;
                days[i].CheckedChanged += (a, b) => { if (!updating) SyncEveryday(); };
                dayPanel.Controls.Add(days[i]);
            }
            DialogLayout.AddFieldRow(fields, "Days", dayPanel);
            SyncEveryday();
            chkEvery.CheckedChanged += (a, b) =>
            {
                if (updating) return;
                updating = true;
                foreach (var d in days) d.Checked = chkEvery.Checked;
                updating = false;
            };

            cmbFrom = MakeTimeCombo(s.ScheduleStartMin);
            cmbTo = MakeTimeCombo(s.ScheduleEndMin);
            DialogLayout.AddFieldRow(fields, "From", cmbFrom);
            DialogLayout.AddFieldRow(fields, "To", cmbTo);

            chkStop = new CheckBox
            {
                Text = "Pause running downloads when the window ends",
                AutoSize = true,
                Checked = s.ScheduleStopOutside,
                Margin = new Padding(0, Theme.Scale(this, 6), 0, Theme.Scale(this, 4))
            };
            DialogLayout.AddFullRow(fields, chkStop);

            var note = new Label
            {
                Text = "A window that crosses midnight works too (for example 22:00 to 06:00). Right-click a download and choose “Start now” to ignore the schedule for it.",
                AutoSize = true,
                MaximumSize = new Size(Theme.Scale(this, 440), 0),
                ForeColor = Theme.P.SubText
            };
            DialogLayout.AddFullRow(fields, note);

            var save = DialogLayout.CreateButton(this, "Save");
            var cancel = DialogLayout.CreateButton(this, "Cancel", DialogResult.Cancel);
            var footer = DialogLayout.CreateFooter(this, cancel, save);
            Controls.Add(DialogLayout.CreateShell(this, fields, footer));
            save.Click += OnOk;
            AcceptButton = save;
            CancelButton = cancel;

            DialogLayout.TrackDirty(this, () => dirty = true);
            FormClosing += (a, e) => DialogLayout.GuardUnsavedChanges(this, dirty, e);
            Shown += (a, e) => chkEnable.Focus();
        }

        void SyncEveryday()
        {
            updating = true;
            bool all = true;
            foreach (var d in days) if (!d.Checked) all = false;
            chkEvery.Checked = all;
            updating = false;
        }

        ComboBox MakeTimeCombo(int minutes)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = Theme.Scale(this, 120),
                AccessibleName = "Schedule time"
            };
            for (int m = 0; m < 1440; m += 15) combo.Items.Add(Scheduler.Hm(m));
            combo.SelectedIndex = Math.Clamp((int)Math.Round(minutes / 15.0), 0, 95);
            return combo;
        }

        void OnOk(object? sender, EventArgs e)
        {
            int mask = 0;
            for (int i = 0; i < 7; i++) if (days[i].Checked) mask |= 1 << i;

            if (chkEnable.Checked && mask == 0)
            {
                ThemeDialog.Show(this, "Select at least one day.");
                chkEnable.Focus();
                return;
            }

            s.SchedulerEnabled = chkEnable.Checked;
            s.ScheduleDays = mask == 0 ? 0x7F : mask;
            s.ScheduleStartMin = cmbFrom.SelectedIndex * 15;
            s.ScheduleEndMin = cmbTo.SelectedIndex * 15;
            s.ScheduleStopOutside = chkStop.Checked;
            dirty = false;
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
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            ClientSize = new Size(460, 170);
            MinimumSize = new Size(380, 150);
            Font = Theme.CreateFont(Theme.FontSection);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(Theme.Scale(this, 20)),
                ColumnCount = 1,
                RowCount = 1
            };
            lbl = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                AccessibleName = "Shutdown countdown"
            };
            body.Controls.Add(lbl, 0, 0);

            var now = DialogLayout.CreateButton(this, "Do it now");
            var cancel = DialogLayout.CreateButton(this, "Cancel", DialogResult.Cancel);
            now.Click += (a, b) => DialogResult = DialogResult.OK;
            var footer = DialogLayout.CreateFooter(this, cancel, now);
            Controls.Add(DialogLayout.CreateShell(this, body, footer));

            // Enter should cancel rather than accidentally trigger shutdown.
            AcceptButton = cancel;
            CancelButton = cancel;

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
