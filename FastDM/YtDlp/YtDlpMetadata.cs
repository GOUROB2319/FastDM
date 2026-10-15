using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FastDM
{
    public sealed class YtVideoOpt
    {
        public string Label = "", Selector = "";
        public string? NoMergeSelector;
        public long EstBytes;
        public int Height;
        public bool RequiresMerge;

        public string? GetSelector(bool ffmpegAvailable) =>
            ffmpegAvailable ? Selector : NoMergeSelector;
    }

    public sealed class YtInfo
    {
        public string Title = "video";
        public string? Extractor, Uploader, Thumbnail;
        public double Duration;
        public bool HasAudio;
        public long BestAudioBytes;
        public List<YtVideoOpt> Videos = new List<YtVideoOpt>();

        // This temporary JSON can contain expiring media URLs. It is moved to a short-lived,
        // per-user cache file before the preview result is retained.
        public string? RawInfoJson;
        public string? InfoJsonPath;
        public DateTimeOffset InfoJsonExpiresAt;
    }

    public sealed class YtPlaylistEntry
    {
        public int Index;
        public string Title = "";
        public string? Url, Id, Uploader;
        public double Duration;
        public bool Available;
    }

    public sealed class YtPlaylistInfo
    {
        public string Title = "Playlist";
        public string? Extractor, Uploader;
        public int? ReportedCount;
        public int EntriesLimit;
        public List<YtPlaylistEntry> Entries = new List<YtPlaylistEntry>();

        public int AvailableCount => Entries.Count(x => x.Available);

        public string CountDescription => ReportedCount.HasValue
            ? EntriesLimit > 0 && Entries.Count >= EntriesLimit && ReportedCount.Value > Entries.Count
                ? "first " + Entries.Count + " listed; playlist reports " + ReportedCount.Value
                : ReportedCount.Value == Entries.Count
                ? Entries.Count + (Entries.Count == 1 ? " video" : " videos")
                : Entries.Count + " listed; playlist reports " + ReportedCount.Value
            : EntriesLimit > 0 && Entries.Count >= EntriesLimit
                ? "first " + Entries.Count + " listed; total unknown"
                : Entries.Count + " entries listed; total unknown";
    }

    public sealed class YtPreviewData
    {
        public YtInfo? Video;
        public YtPlaylistInfo? Playlist;
        public bool IsPlaylist => Playlist != null;
        public string Title => Playlist?.Title ?? Video?.Title ?? "video";
        public string? InfoJsonPath => Video?.InfoJsonPath;
        public DateTimeOffset ExpiresAt;
    }

    public static class YtDlpMetadataParser
    {
        sealed class FormatInfo
        {
            public long Bytes;
            public bool Combined;
            public int Fps;
            public string Ext = "";
            public double Tbr;
        }

        public static YtInfo ParseVideo(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var info = new YtInfo
            {
                Title = String(root, "title") ?? "video",
                Extractor = String(root, "extractor_key") ?? String(root, "extractor"),
                Uploader = String(root, "uploader") ?? String(root, "channel"),
                Duration = Number(root, "duration"),
                Thumbnail = Thumbnail(root)
            };

            if (!root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
                return info;

            var byHeight = new Dictionary<int, FormatInfo>();
            var combinedByHeight = new Dictionary<int, FormatInfo>();
            long bestAudio = 0;
            bool hasAudio = false;

            foreach (var format in formats.EnumerateArray())
            {
                string? videoCodec = String(format, "vcodec");
                string? audioCodec = String(format, "acodec");
                bool hasVideo = !string.IsNullOrEmpty(videoCodec) && videoCodec != "none";
                bool hasFormatAudio = !string.IsNullOrEmpty(audioCodec) && audioCodec != "none";
                long bytes = (long)(Number(format, "filesize") > 0
                    ? Number(format, "filesize")
                    : Number(format, "filesize_approx"));

                if (hasFormatAudio && !hasVideo)
                {
                    hasAudio = true;
                    bestAudio = Math.Max(bestAudio, bytes);
                }
                if (!hasVideo) continue;

                int height = (int)Number(format, "height");
                if (height <= 0) continue;
                var candidate = new FormatInfo
                {
                    Bytes = bytes,
                    Combined = hasFormatAudio,
                    Fps = (int)Number(format, "fps"),
                    Ext = String(format, "ext") ?? "",
                    Tbr = Number(format, "tbr")
                };

                KeepBest(byHeight, height, candidate);
                if (hasFormatAudio) KeepBest(combinedByHeight, height, candidate);
            }

            info.HasAudio = hasAudio;
            info.BestAudioBytes = bestAudio;
            var best = byHeight.OrderByDescending(x => x.Value.Tbr).FirstOrDefault();
            bool hasCombined = combinedByHeight.Count > 0;
            long bestEstimate = best.Value == null
                ? 0
                : best.Value.Bytes + (best.Value.Combined ? 0 : bestAudio);

            if (byHeight.Count > 0)
            {
                info.Videos.Add(new YtVideoOpt
                {
                    Label = "Best available" + (bestEstimate > 0 ? "  •  ≈ " + Fmt(bestEstimate) : ""),
                    Selector = "bv*+ba/b",
                    NoMergeSelector = hasCombined ? "b" : null,
                    EstBytes = bestEstimate,
                    RequiresMerge = best.Value != null && !best.Value.Combined
                });
            }

            foreach (int height in byHeight.Keys.OrderByDescending(x => x))
            {
                var selected = byHeight[height];
                long estimate = selected.Bytes + (selected.Combined ? 0 : bestAudio);
                string label = height + "p" + (selected.Fps > 30 ? selected.Fps.ToString(CultureInfo.InvariantCulture) : "") +
                    (selected.Ext.Length > 0 ? "  •  " + selected.Ext : "") +
                    (estimate > 0 ? "  •  ≈ " + Fmt(estimate) : "") +
                    (!selected.Combined ? "  •  may need merging" : "");
                info.Videos.Add(new YtVideoOpt
                {
                    Label = label,
                    Height = height,
                    EstBytes = estimate,
                    RequiresMerge = !selected.Combined,
                    Selector = "bv*[height<=" + height + "][ext=mp4]+ba[ext=m4a]/bv*[height<=" + height + "]+ba/b[height<=" + height + "]",
                    NoMergeSelector = combinedByHeight.Keys.Any(h => h <= height)
                        ? "b[height<=" + height + "]"
                        : null
                });
            }

            return info;
        }

        public static YtPlaylistInfo ParsePlaylist(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var result = new YtPlaylistInfo
            {
                Title = String(root, "title") ?? String(root, "playlist_title") ?? "Playlist",
                Extractor = String(root, "extractor_key") ?? String(root, "extractor"),
                Uploader = String(root, "uploader") ?? String(root, "channel")
            };

            if (root.TryGetProperty("playlist_count", out var count) &&
                count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int parsedCount) && parsedCount >= 0)
                result.ReportedCount = parsedCount;

            if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return result;

            int index = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                index++;
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    result.Entries.Add(new YtPlaylistEntry { Index = index, Title = "Unavailable video " + index });
                    continue;
                }

                string? id = String(entry, "id");
                string? url = AbsoluteHttpUrl(String(entry, "webpage_url")) ?? AbsoluteHttpUrl(String(entry, "url"));
                string? extractor = String(entry, "ie_key") ?? String(entry, "extractor_key") ?? result.Extractor;
                if (url == null && id != null && IsYouTubeExtractor(extractor))
                    url = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(id);

                bool available = !string.IsNullOrWhiteSpace(url);
                result.Entries.Add(new YtPlaylistEntry
                {
                    Index = index,
                    Id = id,
                    Url = url,
                    Title = String(entry, "title") ?? (available ? "Video " + index : "Unavailable video " + index),
                    Uploader = String(entry, "uploader") ?? String(entry, "channel"),
                    Duration = Number(entry, "duration"),
                    Available = available
                });
            }

            return result;
        }

        public static bool IsYouTubePlaylistUrl(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsYouTubeHost(uri.Host))
                return false;

            if (TryYouTubeVideoId(url, out _)) return false;

            var query = Query(uri.Query);
            if (query.ContainsKey("v") && query["v"].Length > 0)
                return false;
            return query.ContainsKey("list") && query["list"].Length > 0;
        }

        public static string CacheKey(string url)
        {
            if (TryYouTubeVideoId(url, out string? videoId))
                return "youtube:video:" + videoId;
            if (IsYouTubePlaylistUrl(url))
                return "youtube:playlist:" + Query(new Uri(url).Query)["list"];
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return "raw:" + (url ?? "").Trim();

            var builder = new UriBuilder(uri) { Fragment = "" };
            return builder.Uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
        }

        public static bool TryYouTubeVideoId(string? url, out string? id)
        {
            id = null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsYouTubeHost(uri.Host)) return false;
            var query = Query(uri.Query);
            string path = uri.AbsolutePath.Trim('/');
            if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
                id = path.Split('/').FirstOrDefault();
            else if (path.Equals("watch", StringComparison.OrdinalIgnoreCase))
                query.TryGetValue("v", out id);
            else if (path.StartsWith("shorts/", StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith("live/", StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith("embed/", StringComparison.OrdinalIgnoreCase))
                id = path.Split('/').Skip(1).FirstOrDefault();

            return id != null && id.Length == 11 && id.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
        }

        static void KeepBest(Dictionary<int, FormatInfo> formats, int height, FormatInfo candidate)
        {
            if (!formats.TryGetValue(height, out var current) || candidate.Tbr > current.Tbr)
                formats[height] = candidate;
        }

        static string? Thumbnail(JsonElement root)
        {
            string? direct = String(root, "thumbnail");
            if (direct != null) return AbsoluteHttpUrl(direct);
            if (!root.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
                return null;
            return thumbnails.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => AbsoluteHttpUrl(String(x, "url")))
                .LastOrDefault(x => x != null);
        }

        static string? AbsoluteHttpUrl(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.AbsoluteUri
                : null;

        static bool IsYouTubeExtractor(string? extractor) =>
            (extractor ?? "").Contains("youtube", StringComparison.OrdinalIgnoreCase);

        static bool IsYouTubeHost(string host) =>
            host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);

        static Dictionary<string, string> Query(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string name = Decode(eq < 0 ? pair : pair.Substring(0, eq));
                if (name.Length == 0 || result.ContainsKey(name)) continue;
                result[name] = Decode(eq < 0 ? "" : pair.Substring(eq + 1));
            }
            return result;
        }

        static string Decode(string value)
        {
            try { return Uri.UnescapeDataString(value.Replace('+', ' ')); }
            catch { return value; }
        }

        static string? String(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        static double Number(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : 0;

        static string Fmt(double bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
            return (i == 0 ? bytes.ToString("0", CultureInfo.InvariantCulture) : bytes.ToString("0.#", CultureInfo.InvariantCulture)) + " " + units[i];
        }
    }

    public sealed class YtDlpPreviewCache
    {
        sealed class Entry
        {
            public YtPreviewData Data = null!;
            public DateTimeOffset Expires;
        }

        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly string directory;
        readonly TimeSpan lifetime;
        readonly object gate = new object();

        public static YtDlpPreviewCache Shared { get; } = new YtDlpPreviewCache(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastDM", "yt-info"),
            TimeSpan.FromMinutes(2));

        public YtDlpPreviewCache(string directory, TimeSpan lifetime)
        {
            this.directory = directory;
            this.lifetime = lifetime;
            CleanupOldFiles();
        }

        public string? CreateInfoJson(string json, DateTimeOffset now)
        {
            const int maxBytes = 8 * 1024 * 1024;
            if (string.IsNullOrWhiteSpace(json) || json.Length > maxBytes || System.Text.Encoding.UTF8.GetByteCount(json) > maxBytes)
                return null;

            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "info-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
                try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return path;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        static string CacheKey(string url, bool ignoreCookies) =>
            YtDlpMetadataParser.CacheKey(url) + (ignoreCookies ? "|without-cookies" : "|configured-cookies");

        public void Store(string url, YtPreviewData data, DateTimeOffset now, bool ignoreCookies = false)
        {
            lock (gate)
            {
                string key = CacheKey(url, ignoreCookies);
                if (entries.TryGetValue(key, out var old)) DeleteInfoJsonFile(old.Data.InfoJsonPath);

                if (data.Video?.RawInfoJson is string json)
                {
                    data.Video.InfoJsonPath = CreateInfoJson(json, now);
                    data.Video.InfoJsonExpiresAt = now + lifetime;
                    data.Video.RawInfoJson = null;
                }

                data.ExpiresAt = now + lifetime;
                entries[key] = new Entry { Data = data, Expires = data.ExpiresAt };
                Trim(now);
            }
        }

        public bool TryGet(string url, DateTimeOffset now, out YtPreviewData? data, bool ignoreCookies = false)
        {
            lock (gate)
            {
                string key = CacheKey(url, ignoreCookies);
                if (!entries.TryGetValue(key, out var entry))
                {
                    data = null;
                    return false;
                }
                if (entry.Expires <= now ||
                    entry.Data.InfoJsonPath is string path && !File.Exists(path))
                {
                    RemoveKey(key);
                    data = null;
                    return false;
                }
                data = entry.Data;
                return true;
            }
        }

        public void Remove(string url, bool ignoreCookies = false)
        {
            lock (gate) RemoveKey(CacheKey(url, ignoreCookies));
        }

        public void Clear()
        {
            lock (gate)
            {
                foreach (var entry in entries.Values) DeleteInfoJsonFile(entry.Data.InfoJsonPath);
                entries.Clear();
            }
        }

        public static void DeleteInfoJson(string? path)
        {
            Shared.DeleteInfoJsonFile(path);
        }

        public void DeleteInfoJsonFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.GetFullPath(path);
                string cacheRoot = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(full).StartsWith("info-", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetExtension(full).Equals(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                    File.Delete(full);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }

        void Trim(DateTimeOffset now)
        {
            foreach (string key in entries.Where(x => x.Value.Expires <= now).Select(x => x.Key).ToArray())
                RemoveKey(key);
            while (entries.Count > 32)
                RemoveKey(entries.OrderBy(x => x.Value.Expires).First().Key);
        }

        void RemoveKey(string key)
        {
            if (entries.Remove(key, out var entry)) DeleteInfoJsonFile(entry.Data.InfoJsonPath);
        }

        void CleanupOldFiles()
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                foreach (string path in Directory.GetFiles(directory, "info-*.json"))
                    if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddMinutes(-10)) DeleteInfoJsonFile(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public enum YtDlpStage
    {
        Starting,
        Downloading,
        DownloadingVideo,
        DownloadingAudio,
        Merging,
        PostProcessing,
        VerifyingOutput,
        Retrying,
        None
    }

    public sealed class YtDlpProgressUpdate
    {
        public YtDlpStage Stage;
        public long Downloaded, Total;
        public double Speed;
    }

    public sealed class YtDlpProgressParser
    {
        long offset, lastDownloaded, lastTotal;

        public YtDlpStage Stage { get; private set; } = YtDlpStage.Starting;

        public bool TryParseProgress(string line, out YtDlpProgressUpdate update)
        {
            update = new YtDlpProgressUpdate { Stage = Stage };
            if (Stage is YtDlpStage.Merging or YtDlpStage.PostProcessing or YtDlpStage.VerifyingOutput)
                return false;
            if (!line.StartsWith("FDM|", StringComparison.Ordinal)) return false;
            string[] fields = line.Split('|');
            if (fields.Length < 6) return false;

            long downloaded = ParseLong(fields[2]);
            long total = ParseLong(fields[3]);
            if (total <= 0) total = ParseLong(fields[4]);
            if (downloaded < lastDownloaded) offset += lastTotal > 0 ? lastTotal : lastDownloaded;
            lastDownloaded = downloaded;
            lastTotal = total > 0 ? total : downloaded;

            string videoCodec = fields.Length > 6 ? fields[6] : "";
            string audioCodec = fields.Length > 7 ? fields[7] : "";
            Stage = !string.IsNullOrEmpty(videoCodec) && videoCodec != "none" &&
                    !string.IsNullOrEmpty(audioCodec) && audioCodec != "none"
                ? YtDlpStage.Downloading
                : !string.IsNullOrEmpty(videoCodec) && videoCodec != "none"
                    ? YtDlpStage.DownloadingVideo
                    : !string.IsNullOrEmpty(audioCodec) && audioCodec != "none"
                        ? YtDlpStage.DownloadingAudio
                        : YtDlpStage.Downloading;

            update = new YtDlpProgressUpdate
            {
                Stage = Stage,
                Downloaded = offset + downloaded,
                Total = Math.Max(0, offset + lastTotal),
                Speed = ParseDouble(fields[5])
            };
            return true;
        }

        public YtDlpStage ObserveMessage(string line)
        {
            if (line.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase))
                Stage = YtDlpStage.Merging;
            else if (line.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("[VideoRemuxer]", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("[Metadata]", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("[EmbedSubtitle]", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("[Fixup", StringComparison.OrdinalIgnoreCase))
                Stage = YtDlpStage.PostProcessing;
            return Stage;
        }

        public void SetStage(YtDlpStage stage) => Stage = stage;

        static long ParseLong(string value) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? Math.Max(0, result) : 0;

        static double ParseDouble(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? Math.Max(0, result) : 0;
    }
}
