#nullable disable
using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace FastDM
{
    // fastdm:// লিঙ্ক: fastdm://open  বা  fastdm://add?url=<এনকোড করা লিঙ্ক>
    // নিরাপত্তা: এই পথে আসা লিঙ্ক কখনো নিজে থেকে ডাউনলোড শুরু করে না, শুধু Add ডায়ালগ খোলে (ইউজার নিশ্চিত করবে),
    // কারণ যেকোনো ওয়েবপেজ fastdm:// লিঙ্ক ট্রিগার করতে পারে।
    public static class ProtocolHandler
    {
        public class Command
        {
            public string Action;
            public string Url;
        }

        public static string PipeName() => "FastDM.Cmd." + Environment.UserName;

        public static Command Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim().Trim('"');
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var u)) return null;
            if (!u.Scheme.Equals("fastdm", StringComparison.OrdinalIgnoreCase)) return null;

            var cmd = new Command { Action = (u.Host ?? "").ToLowerInvariant() };
            if (cmd.Action == "add")
            {
                var q = ParseQuery(u.Query);
                if (q.TryGetValue("url", out var target) && BridgeAuth.IsAllowedUrl(target))
                    cmd.Url = target;
            }
            return cmd;
        }

        static Dictionary<string, string> ParseQuery(string query)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return d;
            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int i = part.IndexOf('=');
                string k = i < 0 ? part : part.Substring(0, i);
                string v = i < 0 ? "" : part.Substring(i + 1);
                try { d[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v.Replace('+', ' ')); } catch { }
            }
            return d;
        }

        // প্যাকেজ-ছাড়া (পোর্টেবল) ভার্সনে বর্তমান ইউজারের জন্য fastdm:// রেজিস্ট্রেশন।
        // Store (MSIX) ভার্সনে এটা Package.appxmanifest-এর uap:Protocol দিয়ে হয়।
        public static void RegisterForCurrentUser()
        {
            if (UpdateChecker.IsPackaged) return;
            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\fastdm");
            k.SetValue("", "URL:FastDM Protocol");
            k.SetValue("URL Protocol", "");
            using var c = k.CreateSubKey(@"shell\open\command");
            string want = "\"" + exe + "\" \"%1\"";
            if (!string.Equals(c.GetValue("") as string, want, StringComparison.OrdinalIgnoreCase))
                c.SetValue("", want);
        }
    }
}
