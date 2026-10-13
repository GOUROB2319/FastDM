using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FastDM
{
    // প্রক্সি সেটিং ডায়ালগ (গতির সীমা এখন Preferences → Traffic Limits-এ)
    class NetworkForm : Form
    {
        readonly AppSettings s;
        readonly NumericUpDown numPort;
        readonly ComboBox cmbType;
        readonly RadioButton rbNone, rbSystem, rbManual;
        readonly TextBox txtHost, txtUser, txtPass;
        readonly Label lblTest;
        readonly Button btnTest;
        CancellationTokenSource? testCts;

        public NetworkForm(AppSettings settings)
        {
            s = settings;
            Text = "Proxy";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(480, 466);
            Font = new Font("Segoe UI", 9.5f);

            var bold = new Font("Segoe UI", 10f, FontStyle.Bold);

            // ---------- প্রক্সি ----------
            Controls.Add(new Label { Text = "Proxy", Font = bold, Location = new Point(16, 16), AutoSize = true });

            rbNone = new RadioButton { Text = "No proxy", Location = new Point(16, 46), AutoSize = true, Checked = s.Proxy == ProxyMode.None };
            rbSystem = new RadioButton { Text = "Use system proxy", Location = new Point(16, 74), AutoSize = true, Checked = s.Proxy == ProxyMode.System };
            rbManual = new RadioButton { Text = "Manual", Location = new Point(16, 102), AutoSize = true, Checked = s.Proxy == ProxyMode.Manual };
            Controls.AddRange(new Control[] { rbNone, rbSystem, rbManual });

            Controls.Add(new Label { Text = "Type", Location = new Point(40, 140), AutoSize = true });
            cmbType = new ComboBox { Location = new Point(140, 136), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbType.Items.AddRange(new object[] { "HTTP", "SOCKS5" });
            cmbType.SelectedIndex = s.ProxyType == ProxyKind.Socks5 ? 1 : 0;
            Controls.Add(cmbType);

            Controls.Add(new Label { Text = "Host", Location = new Point(40, 176), AutoSize = true });
            txtHost = new TextBox { Location = new Point(140, 172), Width = 310, Text = s.ProxyHost ?? "" };
            Controls.Add(txtHost);

            Controls.Add(new Label { Text = "Port", Location = new Point(40, 212), AutoSize = true });
            numPort = new NumericUpDown
            {
                Location = new Point(140, 208),
                Width = 90,
                Minimum = 1,
                Maximum = 65535,
                Value = Math.Clamp(s.ProxyPort, 1, 65535)
            };
            Controls.Add(numPort);

            Controls.Add(new Label { Text = "Username", Location = new Point(40, 248), AutoSize = true });
            txtUser = new TextBox { Location = new Point(140, 244), Width = 310, Text = s.ProxyUser ?? "" };
            Controls.Add(txtUser);

            Controls.Add(new Label { Text = "Password", Location = new Point(40, 284), AutoSize = true });
            string savedPass = "";
            if (SecretStore.Read("proxy", out _, out var sp)) savedPass = sp ?? "";
            txtPass = new TextBox { Location = new Point(140, 280), Width = 310, UseSystemPasswordChar = true, Text = savedPass };
            Controls.Add(txtPass);

            btnTest = new Button { Text = "Test connection", Location = new Point(140, 322), Size = new Size(130, 32) };
            btnTest.Click += async (a, b) => await RunTestAsync();
            Controls.Add(btnTest);

            lblTest = new Label { Text = "", Location = new Point(280, 329), Size = new Size(190, 22) };
            Controls.Add(lblTest);

            Controls.Add(new Label
            {
                Text = "Notes: the password is stored in Windows Credential Manager. " +
                       "Proxy applies to HTTP/HTTPS downloads; SFTP uses a manual proxy only. " +
                       "FTP connections currently connect directly.",
                Location = new Point(16, 364),
                Size = new Size(450, 52)
            });

            // ---------- বাটন ----------
            var ok = new Button { Text = "OK", Location = new Point(290, 420), Size = new Size(90, 34) };
            var cancel = new Button { Text = "Cancel", Location = new Point(386, 420), Size = new Size(84, 34), DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            rbNone.CheckedChanged += (a, b) => UpdateEnabled();
            rbSystem.CheckedChanged += (a, b) => UpdateEnabled();
            rbManual.CheckedChanged += (a, b) => UpdateEnabled();
            FormClosing += (a, b) => testCts?.Cancel();
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
            testCts = new CancellationTokenSource();
            var token = testCts.Token;

            btnTest.Enabled = false;
            lblTest.Text = "Testing…";
            try
            {
                string msg = await ProxyConfig.TestAsync(CurrentMode, CurrentKind, txtHost.Text,
                    (int)numPort.Value, txtUser.Text.Trim(), txtPass.Text, token);
                if (!IsDisposed) lblTest.Text = msg;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    string m = ex.GetBaseException().Message;
                    lblTest.Text = "Failed: " + (m.Length > 60 ? m.Substring(0, 60) + "…" : m);
                }
            }
            finally
            {
                if (!IsDisposed) btnTest.Enabled = true;
            }
        }

        void OnOk(object? sender, EventArgs e)
        {
            if (CurrentMode == ProxyMode.Manual && txtHost.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "Please enter the proxy host, or choose another proxy option.");
                return;
            }

            s.Proxy = CurrentMode;
            s.ProxyType = CurrentKind;
            s.ProxyHost = txtHost.Text.Trim();
            s.ProxyPort = (int)numPort.Value;
            s.ProxyUser = txtUser.Text.Trim();

            if (s.ProxyUser.Length > 0) SecretStore.Write("proxy", s.ProxyUser, txtPass.Text);
            else SecretStore.Delete("proxy");

            NetworkApply.ApplyAndReset(s);
            DialogResult = DialogResult.OK;
        }
    }
}
