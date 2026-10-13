#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FastDM
{
    public enum BatchKind { Video, Folder, File, Playlist, Invalid }

    // One line of the imported list, after checking it. Nothing here touches the network.
    public sealed class BatchRow
    {
        public int Line;                    // 1-based line number in the text
        public string Text = "";            // the link as it will be used
        public BatchKind Kind;
        public string Note = "";            // what was changed, or why the row is not selected
        public bool Selected;               // pre-selected in the preview
        public bool Duplicate;              // same link as an earlier line
        public bool AlreadyListed;          // already in the download list
    }

    // Video quality for a whole batch. Uses yt-dlp selectors that need no per-video lookup,
    // so a batch can start without reading every video first.
    public sealed class BatchQuality
    {
        public string Label = "";
        public int MaxHeight;               // 0 = best available
        public bool AudioOnly;
        public string AudioFormat = "m4a";

        public string Format =>
            AudioOnly ? "ba/b"
            : MaxHeight <= 0 ? "bv*+ba/b"
            : "bv*[height<=" + MaxHeight + "]+ba/b[height<=" + MaxHeight + "]/bv*+ba/b";

        static BatchQuality Video(string label, int h) => new BatchQuality { Label = label, MaxHeight = h };
        static BatchQuality Audio(string label, string fmt) => new BatchQuality { Label = label, AudioOnly = true, AudioFormat = fmt };

        public static readonly BatchQuality[] Presets =
        {
            Video("Best available", 0),
            Video("Up to 2160p (4K)", 2160),
            Video("Up to 1440p", 1440),
            Video("Up to 1080p", 1080),
            Video("Up to 720p", 720),
            Video("Up to 480p", 480),
            Video("Up to 360p", 360),
            Audio("Audio only (M4A)", "m4a"),
            Audio("Audio only (MP3)", "mp3"),
        };

        public override string ToString() => Label;
    }

    public static class BatchImport
    {
        static readonly Regex LinkRx = new Regex(@"(?:https?|ftp|sftp)://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        static readonly Regex YouTubeIdRx = new Regex(@"^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);
        static readonly Regex PasswordRx = new Regex(@"(?<=//[^/@:\s]+:)[^/@\s]*(?=@)", RegexOptions.Compiled);

        // Reads a text (a .txt file, or lines pasted into the Add window) and checks every line.
        // Blank lines and lines starting with #, // or ; are skipped. Line numbers stay the real ones.
        public static List<BatchRow> Parse(string? text, IEnumerable<string>? existingUrls = null)
        {
            var rows = new List<BatchRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var listed = new HashSet<string>((existingUrls ?? Enumerable.Empty<string>()).Select(Key), StringComparer.OrdinalIgnoreCase);

            string all = (text ?? "").TrimStart('\uFEFF');
            int n = 0;

            foreach (var raw in all.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                n++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//") || line.StartsWith(";"))
                    continue;

                var matches = LinkRx.Matches(line);
                if (matches.Count == 0)
                {
                    rows.Add(new BatchRow
                    {
                        Line = n,
                        Text = line.Length > 200 ? line.Substring(0, 200) + "…" : line,
                        Kind = BatchKind.Invalid,
                        Note = "Not a link (http, https, ftp or sftp)"
                    });
                    continue;
                }

                string url = matches[0].Value.TrimEnd('.', ',', ';', ')', ']', '}');
                var row = new BatchRow { Line = n };
                Classify(row, url);

                if (matches.Count > 1)
                    row.Note = Join(row.Note, "only the first link on the line is used");

                string key = Key(row.Text);
                if (row.Kind != BatchKind.Invalid)
                {
                    if (!seen.Add(key))
                    {
                        row.Duplicate = true;
                        row.Selected = false;
                        row.Note = Join(row.Note, "same link as an earlier line");
                    }
                    else if (listed.Contains(key))
                    {
                        row.AlreadyListed = true;
                        row.Selected = false;
                        row.Note = Join(row.Note, "already in your download list");
                    }
                }

                rows.Add(row);
            }

            return rows;
        }

        static string Join(string a, string b) => a.Length == 0 ? b : a + "; " + b;

        static void Classify(BatchRow row, string url)
        {
            int hash = url.IndexOf('#');
            if (hash >= 0) url = url.Substring(0, hash);
            row.Text = url;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
            {
                row.Kind = BatchKind.Invalid;
                row.Note = "Not a valid link";
                return;
            }

            if (YtDlpSites.IsSupported(url))
            {
                ClassifyVideoSite(row, u);
                return;
            }

            // Same rule the app uses elsewhere: a link ending with "/" is a folder.
            row.Kind = u.AbsolutePath.EndsWith("/") ? BatchKind.Folder : BatchKind.File;
            row.Selected = true;
        }

        static void ClassifyVideoSite(BatchRow row, Uri u)
        {
            string host = u.Host.ToLowerInvariant();
            bool youtube = host == "youtu.be" || host.EndsWith("youtube.com") || host.EndsWith("youtube-nocookie.com");

            if (youtube)
            {
                string? id = YouTubeId(u);
                var query = ParseQuery(u.Query);

                if (id != null)
                {
                    row.Kind = BatchKind.Video;
                    row.Text = "https://www.youtube.com/watch?v=" + id;       // one video only: playlist, index and tracking parts are dropped
                    row.Selected = true;
                    if (query.ContainsKey("list"))
                        row.Note = "playlist part ignored: this one video only";
                    return;
                }

                row.Kind = BatchKind.Playlist;
                row.Selected = false;
                string path = u.AbsolutePath.ToLowerInvariant();
                row.Note = path.StartsWith("/playlist") || query.ContainsKey("list")
                    ? "playlist: not downloaded in a batch"
                    : "channel or page: not a single video";
                return;
            }

            if (u.AbsolutePath.Length <= 1)
            {
                row.Kind = BatchKind.Invalid;
                row.Note = "Not a video link (home page)";
                return;
            }

            row.Kind = BatchKind.Video;
            row.Selected = true;
        }

        static Dictionary<string, string> ParseQuery(string query)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int i = part.IndexOf('=');
                string k = i < 0 ? part : part.Substring(0, i);
                string v = i < 0 ? "" : part.Substring(i + 1);
                try { d[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v.Replace('+', ' ')); } catch { }
            }
            return d;
        }

        // The 11-character YouTube video id of a watch, youtu.be, shorts, live or embed link, else null.
        public static string? YouTubeId(Uri u)
        {
            string host = u.Host.ToLowerInvariant();
            var seg = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string? id = null;

            if (host == "youtu.be")
                id = seg.Length > 0 ? seg[0] : null;
            else if (seg.Length >= 1 && seg[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
                id = ParseQuery(u.Query).TryGetValue("v", out var v) ? v : null;
            else if (seg.Length >= 2 && (seg[0] == "shorts" || seg[0] == "live" || seg[0] == "embed" || seg[0] == "v"))
                id = seg[1];

            return id != null && YouTubeIdRx.IsMatch(id) ? id : null;
        }

        // Same link written differently gives the same key (used to find duplicates).
        public static string Key(string url)
        {
            string s = (url ?? "").Trim();
            int hash = s.IndexOf('#');
            if (hash >= 0) s = s.Substring(0, hash);

            if (Uri.TryCreate(s, UriKind.Absolute, out var u))
            {
                if (YtDlpSites.IsSupported(s))
                {
                    string? id = YouTubeId(u);
                    if (id != null) return "yt:" + id;
                }
                return (u.Scheme + "://" + u.Authority + u.PathAndQuery).TrimEnd('/');
            }
            return s.TrimEnd('/');
        }

        // For the preview: hides a password inside the link (ftp://user:secret@host/ -> ftp://user:***@host/).
        public static string DisplayUrl(string url) => PasswordRx.Replace(url ?? "", "***");

        public static string Summarize(IReadOnlyCollection<BatchRow> rows)
        {
            int videos = rows.Count(r => r.Kind == BatchKind.Video);
            int folders = rows.Count(r => r.Kind == BatchKind.Folder);
            int files = rows.Count(r => r.Kind == BatchKind.File);
            int playlists = rows.Count(r => r.Kind == BatchKind.Playlist);
            int invalid = rows.Count(r => r.Kind == BatchKind.Invalid);
            int dup = rows.Count(r => r.Duplicate);
            int listed = rows.Count(r => r.AlreadyListed);
            int links = rows.Count - invalid;

            var found = new List<string>();
            if (videos > 0) found.Add(videos + (videos == 1 ? " video" : " videos"));
            if (folders > 0) found.Add(folders + (folders == 1 ? " folder" : " folders"));
            if (files > 0) found.Add(files + (files == 1 ? " file" : " files"));

            var skipped = new List<string>();
            if (playlists > 0) skipped.Add(playlists + (playlists == 1 ? " playlist or channel" : " playlists or channels"));
            if (dup > 0) skipped.Add(dup + (dup == 1 ? " duplicate" : " duplicates"));
            if (listed > 0) skipped.Add(listed + " already in the list");
            if (invalid > 0) skipped.Add(invalid + (invalid == 1 ? " line that is not a link" : " lines that are not links"));

            string s = links + (links == 1 ? " link" : " links") + (found.Count > 0 ? ": " + string.Join(", ", found) : "");
            if (skipped.Count > 0) s += ".  Not selected: " + string.Join(", ", skipped);
            return s;
        }
    }
}
