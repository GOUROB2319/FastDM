using FastDM;

namespace FastDM.Tests
{
    static class YtDlpWorkflowTests
    {
        public static void Run()
        {
            MetadataParsing();
            PlaylistParsing();
            PreviewCache();
            RetryAndDiagnostics();
            ProgressStages();
        }

        static void MetadataParsing()
        {
            T.Section("yt-dlp preview metadata parsing");
            const string json = """{"title":"Example lesson","extractor_key":"Youtube","uploader":"Example Channel","duration":125,"thumbnail":"https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg","formats":[{"format_id":"137","vcodec":"avc1","acodec":"none","height":1080,"fps":30,"ext":"mp4","filesize":8000000,"tbr":1800},{"format_id":"140","vcodec":"none","acodec":"mp4a.40.2","ext":"m4a","filesize_approx":2000000,"tbr":128},{"format_id":"18","vcodec":"avc1","acodec":"mp4a.40.2","height":360,"ext":"mp4","filesize":4000000,"tbr":500}]}""";
            var info = YtDlpMetadataParser.ParseVideo(json);
            T.Check("video title, uploader, source, duration and thumbnail are parsed", info.Title == "Example lesson" && info.Uploader == "Example Channel" && info.Extractor == "Youtube" && info.Duration == 125 && info.Thumbnail?.StartsWith("https://") == true);
            T.Check("audio-only formats contribute a size estimate", info.HasAudio && info.BestAudioBytes == 2000000);
            T.Check("video choices retain merge need and estimated bytes", info.Videos.Any(x => x.Height == 1080 && x.RequiresMerge && x.EstBytes == 10000000));
            var merged = info.Videos.First(x => x.Height == 1080);
            var bestAvailable = info.Videos.First(x => x.Height == 0);
            T.Check("known video and audio sizes are summed for quality and best estimates",
                merged.EstBytes == 10000000 && merged.Label.Contains("≈ 9.5 MB", StringComparison.Ordinal) &&
                bestAvailable.EstBytes == 10000000 && bestAvailable.Label.Contains("≈ 9.5 MB", StringComparison.Ordinal));
            T.Check("video option metadata labels contain size but defer merge hints to the picker",
                merged.Label.Contains("≈ 9.5 MB", StringComparison.Ordinal) &&
                !merged.Label.Contains("video + audio merged", StringComparison.Ordinal) &&
                !merged.Label.Contains("single stream (no merge)", StringComparison.Ordinal));
            T.Check("FFmpeg selection chooses merged selector, while no-merge selection is explicit", merged.GetSelector(true) == merged.Selector && merged.GetSelector(false) == "b[height<=1080]");
            T.Check("combined format can be chosen without a merge", info.Videos.Any(x => x.Height == 360 && !x.RequiresMerge && x.GetSelector(false) != null));

            const string unknownSizeJson = """{"formats":[{"vcodec":"avc1","acodec":"none","height":1080,"ext":"mp4","tbr":1800},{"vcodec":"avc1","acodec":"none","height":720,"ext":"mp4","tbr":900},{"vcodec":"none","acodec":"mp4a.40.2","ext":"m4a","filesize_approx":19600000,"tbr":128}]}""";
            var unknownSizeInfo = YtDlpMetadataParser.ParseVideo(unknownSizeJson);
            T.Check("unknown video sizes omit quality and best available estimates despite known audio size",
                unknownSizeInfo.BestAudioBytes == 19600000 && unknownSizeInfo.Videos.Count == 3 && unknownSizeInfo.Videos.All(x =>
                    x.EstBytes == 0 && !x.Label.Contains("≈", StringComparison.Ordinal) &&
                    !x.Label.Contains("~", StringComparison.Ordinal)));

            const string tbrFallbackJson = """{"duration":10,"formats":[{"vcodec":"avc1","acodec":"none","height":1080,"ext":"mp4","tbr":800},{"vcodec":"none","acodec":"mp4a.40.2","ext":"m4a","tbr":128}]}""";
            var tbrFallbackInfo = YtDlpMetadataParser.ParseVideo(tbrFallbackJson);
            T.Check("bitrate and duration estimate video plus audio and mark the result approximate",
                tbrFallbackInfo.Videos.Count == 2 && tbrFallbackInfo.Videos.All(x =>
                    x.EstBytes == 1160000 && x.Label.Contains("~ 1.1 MB", StringComparison.Ordinal) &&
                    !x.Label.Contains("≈", StringComparison.Ordinal)));

            const string tbrWithKnownAudioJson = """{"duration":10,"formats":[{"vcodec":"avc1","acodec":"none","height":720,"ext":"mp4","tbr":800},{"vcodec":"none","acodec":"mp4a.40.2","ext":"m4a","filesize":200000,"tbr":128}]}""";
            var tbrWithKnownAudio = YtDlpMetadataParser.ParseVideo(tbrWithKnownAudioJson);
            T.Check("bitrate fallback adds known audio size instead of estimating it again",
                tbrWithKnownAudio.Videos.All(x => x.EstBytes == 1200000 && x.Label.Contains("~ 1.1 MB", StringComparison.Ordinal)));

            const string unknownEstimateJson = """{"duration":10,"formats":[{"vcodec":"avc1","acodec":"none","height":480,"ext":"mp4"},{"vcodec":"none","acodec":"mp4a.40.2","ext":"m4a"}]}""";
            var unknownEstimate = YtDlpMetadataParser.ParseVideo(unknownEstimateJson);
            T.Check("formats without size or usable bitrate omit the estimate",
                unknownEstimate.Videos.Count == 2 && unknownEstimate.Videos.All(x =>
                    x.EstBytes == 0 && !x.Label.Contains("≈", StringComparison.Ordinal) &&
                    !x.Label.Contains("~", StringComparison.Ordinal)));
        }

        static void PlaylistParsing()
        {
            T.Section("yt-dlp playlist parsing and deduplication keys");
            const string json = """{"title":"Lessons","extractor_key":"Youtube","uploader":"Example Channel","playlist_count":5,"entries":[{"id":"abcdefghijk","title":"First","url":"https://www.youtube.com/watch?v=abcdefghijk","duration":70},{"id":"lmnopqrstuv","title":"Second","url":"https://www.youtube.com/watch?v=lmnopqrstuv"},null]}""";
            var playlist = YtDlpMetadataParser.ParsePlaylist(json);
            T.Check("playlist metadata keeps reported and listed counts", playlist.Title == "Lessons" && playlist.ReportedCount == 5 && playlist.Entries.Count == 3 && playlist.CountDescription.Contains("playlist reports 5"));
            T.Check("unavailable playlist entries remain visible but are not available", playlist.AvailableCount == 2 && !playlist.Entries[2].Available);
            playlist.EntriesLimit = 3;
            T.Check("truncated playlist previews say that only the first entries are listed", playlist.CountDescription.StartsWith("first 3 listed", StringComparison.OrdinalIgnoreCase));
            playlist.ReportedCount = null;
            T.Check("a capped playlist without a reported total is explicitly marked unknown", playlist.CountDescription == "first 3 listed; total unknown");
            T.Check("video links with a playlist parameter are treated as one video", !YtDlpMetadataParser.IsYouTubePlaylistUrl("https://www.youtube.com/watch?v=abcdefghijk&list=PL123"));
            T.Check("standalone playlist links are detected", YtDlpMetadataParser.IsYouTubePlaylistUrl("https://www.youtube.com/playlist?list=PL123"));
            T.Check("short video links with a playlist parameter remain single-video links", !YtDlpMetadataParser.IsYouTubePlaylistUrl("https://youtu.be/abcdefghijk?list=PL123"));
            T.Check("YouTube video URLs deduplicate despite tracking parameters", YtDlpMetadataParser.CacheKey("https://youtu.be/abcdefghijk?t=15") == YtDlpMetadataParser.CacheKey("https://www.youtube.com/watch?v=abcdefghijk&si=tracking"));
            T.Check("playlist keys use the list id", YtDlpMetadataParser.CacheKey("https://youtube.com/playlist?list=PL123") == "youtube:playlist:PL123");
        }

        static void PreviewCache()
        {
            T.Section("yt-dlp preview cache expiry and file cleanup");
            string root = Path.Combine(Path.GetTempPath(), "fastdm-yt-preview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var cache = new YtDlpPreviewCache(root, TimeSpan.FromMinutes(1));
                var now = DateTimeOffset.UtcNow;
                var video = new YtInfo { Title = "Cached", RawInfoJson = "{\"title\":\"Cached\"}" };
                var data = new YtPreviewData { Video = video };
                cache.Store("https://www.youtube.com/watch?v=abcdefghijk", data, now);
                string? infoPath = video.InfoJsonPath;
                T.Check("preview cache stores expiring download metadata outside state.json", infoPath != null && File.Exists(infoPath) && video.RawInfoJson == null);
                string oversizedUnicode = new string('é', 4 * 1024 * 1024 + 1);
                T.Check("preview info JSON is capped by UTF-8 bytes, including multibyte text", cache.CreateInfoJson(oversizedUnicode, now) == null);
                T.Check("fresh preview is reused by normalized URL", cache.TryGet("https://youtu.be/abcdefghijk", now.AddSeconds(10), out var hit) && ReferenceEquals(hit, data));
                var cookieFreeData = new YtPreviewData { Video = new YtInfo { Title = "Cookie-free" } };
                cache.Store("https://youtu.be/abcdefghijk", cookieFreeData, now, ignoreCookies: true);
                T.Check("configured-cookie and explicit cookie-free previews occupy separate cache entries",
                    cache.TryGet("https://youtu.be/abcdefghijk", now, out var configuredHit) && ReferenceEquals(configuredHit, data) &&
                    cache.TryGet("https://www.youtube.com/watch?v=abcdefghijk", now, out var cookieFreeHit, ignoreCookies: true) && ReferenceEquals(cookieFreeHit, cookieFreeData));
                cache.Remove("https://youtu.be/abcdefghijk");
                T.Check("removing the configured-cookie cache leaves the cookie-free retry preview intact",
                    cache.TryGet("https://youtu.be/abcdefghijk", now, out _, ignoreCookies: true));
                T.Check("expired preview is removed with its temporary info JSON", !cache.TryGet("https://youtu.be/abcdefghijk", now.AddMinutes(2), out _) && infoPath != null && !File.Exists(infoPath));

                string outside = Path.Combine(root, "outside.json");
                File.WriteAllText(outside, "keep");
                var nested = new YtDlpPreviewCache(Path.Combine(root, "owned"), TimeSpan.FromMinutes(1));
                nested.DeleteInfoJsonFile(outside);
                T.Check("cache cleanup refuses to delete files outside its own directory", File.Exists(outside));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        static void RetryAndDiagnostics()
        {
            T.Section("yt-dlp failure classification and retry bounds");
            T.Check("HTTP 429 is classified separately", YtDlpDiagnostics.Classify("HTTP Error 429: Too Many Requests") == YtDlpFailureKind.RateLimited);
            T.Check("DNS errors are distinct from generic network errors", YtDlpDiagnostics.Classify("Temporary failure in name resolution") == YtDlpFailureKind.DnsConnection);
            T.Check("HTTP 408 and 5xx responses are treated as temporary network failures", YtDlpDiagnostics.Classify("HTTP Error 503: Service Unavailable") == YtDlpFailureKind.TemporaryNetwork && YtDlpDiagnostics.Classify("HTTP Error 408: Request Timeout") == YtDlpFailureKind.TemporaryNetwork);
            T.Check("bot verification remains distinct from sign-in requirements", YtDlpDiagnostics.Classify("Sign in to confirm you're not a bot") == YtDlpFailureKind.BotVerification && YtDlpDiagnostics.Classify("Sign in to confirm this video") == YtDlpFailureKind.LoginRequired);
            T.Check("configured missing and malformed cookies.txt errors are actionable without exposing its path", YtDlpDiagnostics.Classify("The selected cookies.txt file was not found.") == YtDlpFailureKind.MissingCookieFile && YtDlpDiagnostics.Classify("The selected file is not a readable Netscape-format cookies.txt file.") == YtDlpFailureKind.InvalidCookieFile);
            T.Check("missing visitor data and PO token failures are classified separately", YtDlpDiagnostics.Classify("Missing required visitor data") == YtDlpFailureKind.MissingVisitorData && YtDlpDiagnostics.Classify("PO Token required") == YtDlpFailureKind.PoTokenRequired);
            T.Check("private, geo, format, FFmpeg, merge, disk and permission failures are classified", YtDlpDiagnostics.Classify("This video is private") == YtDlpFailureKind.UnsupportedVideo && YtDlpDiagnostics.Classify("Not available in your country") == YtDlpFailureKind.GeoRestricted && YtDlpDiagnostics.Classify("Requested format is not available") == YtDlpFailureKind.FormatUnavailable && YtDlpDiagnostics.Classify("ffmpeg was not found") == YtDlpFailureKind.MissingFfmpeg && YtDlpDiagnostics.Classify("Merger error") == YtDlpFailureKind.MergeFailed && YtDlpDiagnostics.Classify("No space left on device") == YtDlpFailureKind.InsufficientDiskSpace && YtDlpDiagnostics.Classify("Access is denied") == YtDlpFailureKind.PermissionDenied);
            T.Check("HTTP Retry-After seconds are parsed and capped", YtDlpRetryPolicy.RetryAfter("Retry-After: 45", DateTimeOffset.UtcNow) == TimeSpan.FromSeconds(45) && YtDlpRetryPolicy.RetryAfter("Retry-After: 900", DateTimeOffset.UtcNow) == TimeSpan.FromMinutes(5));
            T.Check("rate limits are not automatically retried without Retry-After and retry only once with it", !YtDlpRetryPolicy.ShouldAutoRetry(YtDlpFailureKind.RateLimited, 0, null) && YtDlpRetryPolicy.ShouldAutoRetry(YtDlpFailureKind.RateLimited, 0, TimeSpan.FromSeconds(45)) && !YtDlpRetryPolicy.ShouldAutoRetry(YtDlpFailureKind.RateLimited, 1, TimeSpan.FromSeconds(45)));
            T.Check("transient connection errors use bounded application retries", YtDlpRetryPolicy.ShouldAutoRetry(YtDlpFailureKind.TemporaryNetwork, 0, null) && !YtDlpRetryPolicy.ShouldAutoRetry(YtDlpFailureKind.TemporaryNetwork, 3, null) && !YtDlpRetryPolicy.ShouldAutoRetry(YtDlpFailureKind.NetworkOrAccess, 0, null));
            T.Check("retry command arguments use bounded attempts", YtDlpRetryPolicy.Arguments.Contains("3") && !YtDlpRetryPolicy.Arguments.Contains("10"));
            T.Check("expired media URLs trigger at most one info refresh", YtDlpRetryPolicy.ShouldRefreshInfoJson(YtDlpFailureKind.ExpiredMediaUrl, "") && YtDlpRetryPolicy.ShouldRefreshInfoJson(YtDlpFailureKind.NetworkOrAccess, "HTTP Error 403: Forbidden"));
            var safeError = new YtDlpProcessException(1, "ERROR: HTTP Error 403: https://www.youtube.com/watch?v=private-token");
            T.Check("yt-dlp exception summaries classify failures and redact URL query values", safeError.FailureKind == YtDlpFailureKind.NetworkOrAccess && !safeError.Message.Contains("private-token", StringComparison.Ordinal));
        }

        static void ProgressStages()
        {
            T.Section("yt-dlp progress and merge stage reporting");
            var parser = new YtDlpProgressParser();
            T.Check("video transfer parses stage and bytes", parser.TryParseProgress("FDM|downloading|100|1000|0|10|avc1|none", out var video) && video.Stage == YtDlpStage.DownloadingVideo && video.Downloaded == 100 && video.Total == 1000);
            T.Check("audio transfer keeps cumulative byte progress", parser.TryParseProgress("FDM|downloading|50|500|0|10|none|mp4a", out var audio) && audio.Stage == YtDlpStage.DownloadingAudio && audio.Downloaded == 1050);
            YtDlpStage merging = parser.ObserveMessage("[Merger] Merging formats into \"output.mp4\"");
            T.Check("merge and post-processing stages stop transfer speed and ETA", merging == YtDlpStage.Merging && !YtDlpRetryPolicy.IsTransferring(merging) && YtDlpRetryPolicy.StageLabel(merging) == "Merging video and audio");
            T.Check("merge and verification are treated as processing rather than transfer", YtDlpRetryPolicy.IsProcessing(merging) && YtDlpRetryPolicy.IsProcessing(YtDlpStage.VerifyingOutput));
            T.Check("late progress callbacks cannot move a merge back to downloading", !parser.TryParseProgress("FDM|downloading|200|1000|0|100|avc1|none", out _) && parser.Stage == YtDlpStage.Merging);
            T.Check("verification is a separate completion stage", YtDlpRetryPolicy.StageLabel(YtDlpStage.VerifyingOutput) == "Checking output file");
        }
    }
}
