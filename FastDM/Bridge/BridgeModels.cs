#nullable disable
using System.Collections.Generic;

namespace FastDM
{
    // এক্সটেনশন → অ্যাপ: নতুন ডাউনলোডের অনুরোধ
    // Phase 1-এ শুধু Url ব্যবহার হয়; বাকি ফিল্ড প্রোটোকলের জন্য রাখা (Phase 2-এ কুকি/Referer প্রয়োগ হবে)
    public class BridgeAddRequest
    {
        public string Url { get; set; }
        public string Kind { get; set; } = "auto";      // auto | file | folder | media | page
        public string Title { get; set; }
        public string Referer { get; set; }
        public string UserAgent { get; set; }
    }

    // অ্যাপ → এক্সটেনশন: ডাউনলোডের অবস্থা (পপআপে দেখানোর জন্য)
    public class BridgeTaskInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string State { get; set; }               // Queued | Downloading | Paused | Complete | Error
        public double Percent { get; set; }
        public double Speed { get; set; }               // bytes/sec
        public long Size { get; set; }
        public long Downloaded { get; set; }
        public string Eta { get; set; }
    }

    // অ্যাপের যে অংশ bridge-কে সেবা দেয় (Form1 এটা বাস্তবায়ন করে)
    public interface IBridgeHost
    {
        AppSettings BridgeSettings { get; }
        System.Threading.Tasks.Task<bool> AskPairAsync(string origin);
        void ExternalAdd(string url, string title);
        List<BridgeTaskInfo> GetTasks();
        bool TaskAction(string id, string action);       // pause | resume
        void SaveSettings();
    }
}
