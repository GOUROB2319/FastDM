using System.Collections.Generic;

namespace FastDM
{
    // এক্সটেনশন → অ্যাপ: নতুন ডাউনলোডের অনুরোধ
    // Phase 2: Mode, Referer, UserAgent আর Cookies এখন সত্যিই প্রয়োগ হয়।
    public class BridgeAddRequest
    {
        public string Url { get; set; } = string.Empty;
        public string Kind { get; set; } = "auto";      // auto | file | folder | media | page
        public string Mode { get; set; } = "open";      // open = Add ডায়ালগ (ডিফল্ট) | download = ডায়ালগ ছাড়া শুরু | playlist = ফোল্ডার উইন্ডো প্লেলিস্ট মোডে
        public string? Title { get; set; }
        public string? Referer { get; set; }
        public string? UserAgent { get; set; }
        public List<BridgeCookie>? Cookies { get; set; }
    }

    // ব্রাউজারের কুকি (chrome.cookies.getAll-এর ফল)। শুধু মেমোরিতে রাখা হয়, ডিস্কে সেভ হয় না।
    public class BridgeCookie
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool Secure { get; set; }
    }

    // একটা অনুরোধের সাথে আসা অতিরিক্ত তথ্য (Add ডায়ালগ বা সরাসরি ডাউনলোড, দুই পথেই লাগে)
    public class RequestContext
    {
        public string? Referer { get; set; }
        public string? UserAgent { get; set; }
    }

    // অ্যাপ → এক্সটেনশন: ডাউনলোডের অবস্থা (পপআপে দেখানোর জন্য)
    public class BridgeTaskInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;               // Queued | Downloading | Paused | Complete | Error
        public double Percent { get; set; }
        public double Speed { get; set; }               // bytes/sec
        public long Size { get; set; }
        public long Downloaded { get; set; }
        public string? Eta { get; set; }
    }

    // অ্যাপের যে অংশ bridge-কে সেবা দেয় (Form1 এটা বাস্তবায়ন করে)
    public interface IBridgeHost
    {
        AppSettings BridgeSettings { get; }
        System.Threading.Tasks.Task<bool> AskPairAsync(string origin);
        void ExternalAdd(BridgeAddRequest add);       // Mode অনুযায়ী ডায়ালগ খোলে বা সরাসরি ডাউনলোড শুরু করে
        List<BridgeTaskInfo> GetTasks();
        bool TaskAction(string id, string action);       // pause | resume
        void SaveSettings();
    }
}
