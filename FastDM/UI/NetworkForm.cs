using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FastDM
{
    // Proxy preferences dialog. Networking and settings application behavior remain unchanged.
    class NetworkForm : Form
    {
        readonly AppSettings s;
        readonly NumericUpDown numPort;
        readonly ComboBox cmbType;
        readonly RadioButton rbNone, rbSystem, rbManual;
        readonly TextBox txtHost, txtUser, txtPass;
        readonly Label lblTest;
        readonly Button btnTest;
        readonly ToolTip tips = new ToolTip();
        CancellationTokenSource? testCts;
        bool dirty;

        public NetworkForm(AppSettings settings)
        {
            Theme.ConfigureForm(this);
            s = settings;
            Text = "Proxy";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 500);
            MinimumSize = new Size(460, 430);
            Font = Theme.CreateFont(Theme.FontBody);

            var fields = DialogLayout.CreateTwoColumnFields(this, labelWidth: 120);

            var mode = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = Padding.Empty
            };
            rbNone = new RadioButton { Text = "No proxy", AutoSize = true, Checked = s.Proxy == ProxyMode.None, Margin = new Padding(0, 3, Theme.Scale(this, 16), 3) };
            rbSystem = new RadioButton { Text = "Use system proxy", AutoSize = true, Checked = s.Proxy == ProxyMode.System, Margin = new Padding(0, 3, Theme.Scale(this, 16), 3) };
            rbManual = new RadioButton { Text = "Manual", AutoSize = true, Checked = s.Proxy == ProxyMode.Manual, Margin = new Padding(0, 3, 0, 3) };
            mode.Controls.AddRange(new Control[] { rbNone, rbSystem, rbManual });
            DialogLayout.AddFullRow(fields, mode);

            cmbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbType.Items.AddRange(new object[] { "HTTP", "SOCKS5" });
            cmbType.SelectedIndex = s.ProxyType == ProxyKind.Socks5 ? 1 : 0;
            DialogLayout.AddFieldRow(fields, "Type", cmbType);

            txtHost = new TextBox { Text = s.ProxyHost ?? "", AccessibleName = "Proxy host" };
            DialogLayout.AddFieldRow(fields, "Host", txtHost);

            numPort = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 65535,
                Value = Math.Clamp(s.ProxyPort, 1, 65535),
                Width = Theme.Scale(this, 120),
                AccessibleName = "Proxy port"
            };
            DialogLayout.AddFieldRow(fields, "Port", numPort);

            txtUser = new TextBox { Text = s.ProxyUser ?? "", AccessibleName = "Proxy username" };
            DialogLayout.AddFieldRow(fields, "Username", txtUser);

            string savedPass = "";
            if (SecretStore.Read("proxy", out _, out var sp)) savedPass = sp ?? "";
            txtPass = new TextBox { UseSystemPasswordChar = true, Text = savedPass, AccessibleName = "Proxy password" };
            DialogLayout.AddFieldRow(fields, "Password", txtPass);

            var testRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty
            };
            testRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            testRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            btnTest = DialogLayout.CreateButton(this, "Test connection");
            btnTest.Width = Math.Max(btnTest.Width, Theme.Scale(this, 132));
            btnTest.Click += async (a, b) =>
            {
                try { await RunTestAsync(); }
                catch (OperationCanceledException) { AppLog.Write("NetworkForm: test cancelled"); }
                catch (Exception exGuard) { Program.ReportError("NetworkForm test", exGuard); }
            };
            lblTest = new Label
            {
                Text = "",
                AutoSize = true,
                MaximumSize = new Size(Theme.Scale(this, 440), 0),
                Margin = new Padding(0, Theme.Scale(this, 8), 0, Theme.Scale(this, 4)),
                AccessibleName = "Connection test result",
                AccessibleRole = AccessibleRole.Alert
            };
            lblTest.ContextMenuStrip = new ContextMenuStrip();
            lblTest.ContextMenuStrip.Items.Add("Copy", null, (a, b) =>
            {
                try { Clipboard.SetText(AppLog.Redact(lblTest.Text)); }
                catch (Exception ex) { ThemeDialog.ShowError(this, "Copy result", "The test result could not be copied.", ex); }
            });
            testRow.Controls.Add(btnTest, 0, 0);
            testRow.Controls.Add(lblTest, 0, 1);
            DialogLayout.AddFullRow(fields, testRow);
            tips.SetToolTip(lblTest, "Right-click to copy the complete redacted test result.");

            var notes = new Label
            {
                Text = "The password is stored in Windows Credential Manager. Proxy applies to HTTP/HTTPS downloads; SFTP uses a manual proxy only. FTP connections currently connect directly.",
                AutoSize = true,
                MaximumSize = new Size(Theme.Scale(this, 440), 0),
                ForeColor = Theme.P.SubText,
                Margin = new Padding(0, Theme.Scale(this, 6), 0, Theme.Scale(this, 8))
            };
            DialogLayout.AddFullRow(fields, notes);

            var save = DialogLayout.CreateButton(this, "Save", DialogResult.None);
            var cancel = DialogLayout.CreateButton(this, "Cancel", DialogResult.Cancel);
            var footer = DialogLayout.CreateFooter(this, cancel, save);
            Controls.Add(DialogLayout.CreateShell(this, fields, footer));
            save.Click += OnOk;
            AcceptButton = save;
            CancelButton = cancel;

            rbNone.CheckedChanged += (a, b) => UpdateEnabled();
            rbSystem.CheckedChanged += (a, b) => UpdateEnabled();
            rbManual.CheckedChanged += (a, b) => UpdateEnabled();
            DialogLayout.TrackDirty(this, () => dirty = true);
            FormClosing += (a, e) =>
            {
                testCts?.Cancel();
                DialogLayout.GuardUnsavedChanges(this, dirty, e);
            };
            Shown += (a, e) =>
            {
                if (rbManual.Checked) DialogLayout.FocusFirstEditable(fields);
                else rbNone.Focus();
            };
            Resize += (a, e) =>
            {
                lblTest.MaximumSize = new Size(Math.Max(Theme.Scale(this, 160), fields.ClientSize.Width - Theme.Scale(this, 40)), 0);
                notes.MaximumSize = new Size(Math.Max(Theme.Scale(this, 160), fields.ClientSize.Width - Theme.Scale(this, 40)), 0);
            };
            UpdateEnabled();
        }

        void UpdateEnabled()
        {
            bool manual = rbManual.Checked;
            cmbType.Enabled = txtHost.Enabled = numPort.Enabled = txtUser.Enabled = txtPass.Enabled = manual;
        }

        ProxyMode CurrentMode =>
            rbNone.Checked ? ProxyMode.None : rbManual.Checked ? ProxyMode.Manual : ProxyMode.System;

        ProxyKind CurrentKind => cmbType.SelectedIndex == 1 ? ProxyKind.Socks5 : ProxyKind.Http;

        async Task RunTestAsync()
        {
            testCts?.Cancel();
            testCts?.Dispose();
            testCts = new CancellationTokenSource();
            var token = testCts.Token;

            btnTest.Enabled = false;
            SetTestResult("Testing…", Theme.P.SubText);
            try
            {
                string msg = await ProxyConfig.TestAsync(CurrentMode, CurrentKind, txtHost.Text,
                    (int)numPort.Value, txtUser.Text.Trim(), txtPass.Text, token);
                if (!IsDisposed) SetTestResult(AppLog.Redact(msg), Theme.P.Success);
            }
            catch (OperationCanceledException) { AppLog.Write("NetworkForm: connection test cancelled"); }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    SetTestResult("Failed: " + AppLog.Redact(ex.GetBaseException().Message), Theme.P.Error);
            }
            finally
            {
                if (!IsDisposed) btnTest.Enabled = true;
            }
        }

        void SetTestResult(string message, Color color)
        {
            lblTest.Text = AppLog.Redact(message ?? string.Empty);
            lblTest.ForeColor = color;
            tips.SetToolTip(lblTest, lblTest.Text.Length == 0
                ? "Right-click to copy the complete redacted test result."
                : AppLog.Redact(lblTest.Text) + Environment.NewLine + "Right-click to copy.");
            lblTest.PerformLayout();
            lblTest.Parent?.PerformLayout();
        }

        void OnOk(object? sender, EventArgs e)
        {
            if (CurrentMode == ProxyMode.Manual && txtHost.Text.Trim().Length == 0)
            {
                ThemeDialog.Show(this, "Please enter the proxy host, or choose another proxy option.");
                txtHost.Focus();
                return;
            }

            string selectedUser = txtUser.Text.Trim();
            if (selectedUser.Length > 0 && !SecretStore.Write("proxy", selectedUser, txtPass.Text))
            {
                ThemeDialog.Show(this,
                    "Windows Credential Manager could not save the proxy password. The proxy settings were not applied.",
                    "Proxy password was not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (selectedUser.Length == 0 && !SecretStore.Delete("proxy"))
            {
                ThemeDialog.Show(this,
                    "FastDM could not remove the saved proxy password. The proxy settings were not applied.",
                    "Saved password could not be removed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            s.Proxy = CurrentMode;
            s.ProxyType = CurrentKind;
            s.ProxyHost = txtHost.Text.Trim();
            s.ProxyPort = (int)numPort.Value;
            s.ProxyUser = txtUser.Text.Trim();

            NetworkApply.ApplyAndReset(s);
            dirty = false;
            DialogResult = DialogResult.OK;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
                testCts?.Cancel();
                testCts?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
