using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FastDM
{
    public class PlaylistEntry
    {
        public string SortKey = string.Empty;      // রিলেটিভ পাথ (ফোল্ডারসহ), প্রাকৃতিক ক্রমে সাজানোর জন্য
        public string Title = string.Empty;
        public string Target = string.Empty;       // সার্ভারের লিঙ্ক অথবা লোকাল রিলেটিভ পাথ
    }

    public class PlaylistResult
    {
        public string Text = string.Empty;
        public int Count;           // প্লেলিস্টে যত ফাইল গেছে
        public int Skipped;         // ভিডিও/অডিও নয় বলে বাদ
    }

    // VLC-র জন্য .m3u8 প্লেলিস্ট। দুই ধরন:
    //   Stream: সার্ভারের লিঙ্ক (ডাউনলোড ছাড়াই চলে), লিঙ্কে ইউজারনেম-পাসওয়ার্ড বসে না
    //   Local : ডাউনলোড ফোল্ডারের রিলেটিভ পাথ (ফাইল নামলে অফলাইনে চলে)
    public static class PlaylistBuilder
    {
        static readonly HashSet<string> MediaExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".flv", ".webm", ".ts", ".m2ts",
            ".mpg", ".mpeg", ".3gp", ".ogv", ".vob",
            ".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus", ".wma"
        };

        public static bool IsMedia(string name) =>
            MediaExt.Contains(Path.GetExtension(name ?? ""));

        public static PlaylistResult BuildStream(IEnumerable<PickedFile> files)
        {
            var entries = new List<PlaylistEntry>();
            int skipped = 0;

            foreach (var f in files)
            {
                if (!IsMedia(f.Name)) { skipped++; continue; }

                entries.Add(new PlaylistEntry
                {
                    SortKey = Path.Combine(f.RelDir ?? "", f.Name),
                    Title = Path.GetFileNameWithoutExtension(f.Name),
                    Target = StripCredentials(f.Url)
                });
            }
            return Build(entries, skipped);
        }

        // relPath: ডাউনলোড ফোল্ডারের ভেতরের পাথ, যেমন "Season 1\Ep 01.mkv"
        public static PlaylistResult BuildLocal(IEnumerable<string> relPaths)
        {
            var entries = new List<PlaylistEntry>();
            int skipped = 0;

            foreach (var rel in relPaths)
            {
                if (!IsMedia(rel)) { skipped++; continue; }

                entries.Add(new PlaylistEntry
                {
                    SortKey = rel,
                    Title = Path.GetFileNameWithoutExtension(rel),
                    Target = rel.Replace('\\', '/')
                });
            }
            return Build(entries, skipped);
        }

        static PlaylistResult Build(List<PlaylistEntry> entries, int skipped)
        {
            entries.Sort((a, b) => NaturalComparer.Instance.Compare(Key(a), Key(b)));

            var sb = new StringBuilder("#EXTM3U\r\n");
            foreach (var e in entries)
            {
                sb.Append("#EXTINF:-1,").Append(OneLine(e.Title)).Append("\r\n");
                sb.Append(OneLine(e.Target)).Append("\r\n");
            }
            return new PlaylistResult { Text = sb.ToString(), Count = entries.Count, Skipped = skipped };
        }

        // ফোল্ডারের বিভাজক সবার আগে বসুক, যাতে "Season 1\" আর "Season 1 extras\" মিশে না যায়
        static string Key(PlaylistEntry e) => (e.SortKey ?? "").Replace('\\', '\u0001').Replace('/', '\u0001');

        static string OneLine(string s) => (s ?? "").Replace('\r', ' ').Replace('\n', ' ');

        public static void Save(string path, string text) =>
            File.WriteAllText(path, text, new UTF8Encoding(false));

        // লিঙ্ক থেকে user:pass@ বাদ (প্লেলিস্ট ফাইলে পাসওয়ার্ড খোলা থাকবে না, VLC দরকারে নিজে জিজ্ঞেস করবে)
        public static string StripCredentials(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || string.IsNullOrEmpty(u.UserInfo))
                return url;

            var b = new UriBuilder(u) { UserName = "", Password = "" };
            return b.Uri.AbsoluteUri;
        }
    }

    // "Episode 2" "Episode 10"-এর আগে আসবে (সংখ্যা সংখ্যার মতো তুলনা)
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();

        static bool Digit(char c) => c >= '0' && c <= '9';

        public int Compare(string? a, string? b)
        {
            a ??= ""; b ??= "";
            int i = 0, j = 0;

            while (i < a.Length && j < b.Length)
            {
                if (Digit(a[i]) && Digit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && Digit(a[i])) i++;
                    while (j < b.Length && Digit(b[j])) j++;

                    string na = a.Substring(si, i - si).TrimStart('0');
                    string nb = b.Substring(sj, j - sj).TrimStart('0');

                    if (na.Length != nb.Length) return na.Length < nb.Length ? -1 : 1;
                    int d = string.CompareOrdinal(na, nb);
                    if (d != 0) return d;
                }
                else
                {
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
