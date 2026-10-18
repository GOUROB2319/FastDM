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
    // MainForm part: browser-extension bridge and single-instance pipe.
    public partial class MainForm
    {
        // ---------- ব্রাউজার এক্সটেনশন bridge ----------
        void StartBridge()
        {
            try
            {
                if (!settings.BridgeEnabled ||
                    bridge != null)
                    return;

                var b =
                    new BridgeServer(
                        new BridgeHostAdapter(this));

                if (b.Start())
                    bridge = b;
                else
                    b.Dispose();
            }
            catch
            {
                bridge = null;
            }
        }

        void StopBridge()
        {
            try
            {
                bridge?.Dispose();
            }
            catch (Exception exIgnored1) { AppLog.Write("MainForm.Bridge (ignored): " + exIgnored1.Message); }

            bridge = null;
        }

        sealed class BridgeHostAdapter : IBridgeHost
        {
            readonly MainForm f;

            public BridgeHostAdapter(MainForm form)
            {
                f = form;
            }

            public AppSettings BridgeSettings =>
                f.settings;

            public Task<bool> AskPairAsync(
                string origin)
            {
                var tcs =
                    new TaskCompletionSource<bool>();

                try
                {
                    f.BeginInvoke(
                        new Action(() =>
                        {
                            f.RestoreFromTray();

                            var r =
                                ThemeDialog.Show(
                                    f,
                                    "A browser extension wants to connect to FastDM.\n\nOrigin: " +
                                    origin +
                                    "\n\nAllow it to send downloads to FastDM?",
                                    "FastDM — browser extension",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Question,
                                    MessageBoxDefaultButton.Button2);

                            tcs.TrySetResult(
                                r == DialogResult.Yes);
                        }));
                }
                catch
                {
                    tcs.TrySetResult(false);
                }

                return tcs.Task;
            }

            public void ExternalAdd(
                BridgeAddRequest add)
            {
                try
                {
                    var ctx = new RequestContext
                    {
                        Referer = add.Referer,
                        UserAgent = add.UserAgent,
                        Cookies = add.Cookies?.ToList()
                    };

                    bool direct =
                        string.Equals(
                            add.Mode,
                            "download",
                            StringComparison.OrdinalIgnoreCase);

                    bool playlist =
                        string.Equals(
                            add.Mode,
                            "playlist",
                            StringComparison.OrdinalIgnoreCase);

                    f.BeginInvoke(
                        new Action(() =>
                        {
                            if (direct)
                            {
                                f.QuickAdd(add.Url, ctx);
                            }
                            else if (playlist)
                            {
                                f.RestoreFromTray();
                                f.OpenFolderForPlaylist(add.Url);
                            }
                            else
                            {
                                f.RestoreFromTray();
                                f.ShowAddDialog(add.Url, ctx);
                            }
                        }));
                }
                catch (Exception exIgnored2) { AppLog.Write("MainForm.Bridge (ignored): " + exIgnored2.Message); }
            }

            public List<BridgeTaskInfo> GetTasks()
            {
                try
                {
                    return (List<BridgeTaskInfo>)f.Invoke(
                        new Func<List<BridgeTaskInfo>>(
                            () =>
                                f.items
                                 .OrderByDescending(
                                     i => i.Added)
                                 .Take(30)
                                 .Select(
                                     i =>
                                         new BridgeTaskInfo
                                         {
                                             Id = i.Id,
                                             Name = i.FileName,
                                             State =
                                                 i.State ==
                                                     DlState.Completed
                                                     ? "Complete"
                                                     : i.State.ToString(),
                                             Percent =
                                                 i.Percent,
                                             Speed =
                                                 i.Speed,
                                             Size =
                                                 i.TotalBytes,
                                             Downloaded =
                                                 i.Downloaded,
                                             Eta =
                                                 Eta(i)
                                         })
                                 .ToList()));
                }
                catch
                {
                    return new List<BridgeTaskInfo>();
                }
            }

            public bool TaskAction(
                string id,
                string action)
            {
                try
                {
                    return (bool)f.Invoke(
                        new Func<bool>(() =>
                        {
                            var it =
                                f.items.FirstOrDefault(
                                    x => x.Id == id);

                            if (it == null)
                                return false;

                            if (action == "pause")
                            {
                                if (it.State ==
                                    DlState.Downloading)
                                {
                                    it.Cts?.Cancel();
                                }
                                else if (
                                    it.State ==
                                    DlState.Queued)
                                {
                                    it.State =
                                        DlState.Paused;
                                }
                            }
                            else if (
                                action == "resume")
                            {
                                if (
                                    it.State ==
                                        DlState.Paused ||
                                    it.State ==
                                        DlState.Error)
                                {
                                    it.State =
                                        DlState.Queued;

                                    it.Error = null;
                                }
                            }

                            f.MarkChanged();

                            return true;
                        }));
                }
                catch
                {
                    return false;
                }
            }

            public void SaveSettings()
            {
                try
                {
                    f.BeginInvoke(
                        new Action(
                            f.SaveState));
                }
                catch (Exception exIgnored3) { AppLog.Write("MainForm.Bridge (ignored): " + exIgnored3.Message); }
            }
        }

        // ---------- fastdm:// কমান্ড ----------
        void StartPipeListener()
        {
            pipeCts =
                new CancellationTokenSource();

            var token =
                pipeCts.Token;

            string name =
                ProtocolHandler.PipeName();

            _ = Task.Run(
                async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            using var server =
                                new NamedPipeServerStream(
                                    name,
                                    PipeDirection.In,
                                    1,
                                    PipeTransmissionMode.Byte,
                                    PipeOptions.Asynchronous);

                            await server
                                .WaitForConnectionAsync(token);

                            using var sr =
                                new StreamReader(
                                    server,
                                    Encoding.UTF8);

                            string? line =
                                await sr.ReadLineAsync();

                            if (!string.IsNullOrWhiteSpace(line) &&
                                IsHandleCreated &&
                                !IsDisposed)
                            {
                                BeginInvoke(
                                    new Action(
                                        () =>
                                            HandleProtocolCommand(
                                                line)));
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch
                        {
                            try
                            {
                                await Task.Delay(
                                    500,
                                    token);
                            }
                            catch
                            {
                                break;
                            }
                        }
                    }
                });
        }

        void HandleProtocolCommand(
            string raw)
        {
            var cmd =
                ProtocolHandler.Parse(raw);

            if (cmd == null)
                return;

            RestoreFromTray();

            if (cmd.Action == "add" &&
                !string.IsNullOrEmpty(cmd.Url))
            {
                ShowAddDialog(
                    cmd.Url);
            }
            else if (cmd.Action == "playlist" &&
                     !string.IsNullOrEmpty(cmd.Url))
            {
                // fastdm:// যেকোনো ওয়েবপেজ ট্রিগার করতে পারে, তাই স্ক্যানের আগে ইউজারের হ্যাঁ লাগবে
                var ask =
                    ThemeDialog.Show(
                        this,
                        "A web link asks FastDM to scan this folder and make a playlist:\n\n" +
                        cmd.Url +
                        "\n\nContinue?",
                        "FastDM",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);

                if (ask == DialogResult.Yes)
                    OpenFolderForPlaylist(
                        cmd.Url);
            }
        }

        void SetupSingleInstanceListener()
        {
            try
            {
                showEvent =
                    new EventWaitHandle(
                        false,
                        EventResetMode.AutoReset,
                        @"Local\FastDM.Show");

                regWait =
                    ThreadPool.RegisterWaitForSingleObject(
                        showEvent,
                        (state, timedOut) =>
                        {
                            try
                            {
                                if (!IsDisposed &&
                                    IsHandleCreated)
                                {
                                    BeginInvoke(
                                        new Action(
                                            RestoreFromTray));
                                }
                            }
                            catch (Exception exIgnored4) { AppLog.Write("MainForm.Bridge (ignored): " + exIgnored4.Message); }
                        },
                        null,
                        Timeout.Infinite,
                        false);
            }
            catch (Exception exIgnored5) { AppLog.Write("MainForm.Bridge (ignored): " + exIgnored5.Message); }
        }
    }
}
