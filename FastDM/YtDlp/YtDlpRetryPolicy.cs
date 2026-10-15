using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FastDM
{
    public static class YtDlpRetryPolicy
    {
        public static readonly string[] Arguments =
        {
            "--retries", "3",
            "--fragment-retries", "3",
            "--retry-sleep", "http:exp=1:8",
            "--retry-sleep", "fragment:exp=1:8",
            "--retry-sleep", "extractor:exp=1:8"
        };

        public static bool ShouldRefreshInfoJson(YtDlpFailureKind kind, string? diagnostic) =>
            kind == YtDlpFailureKind.ExpiredMediaUrl ||
            kind == YtDlpFailureKind.NetworkOrAccess &&
            (diagnostic ?? "").Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase) ||
            (diagnostic ?? "").Contains("info json", StringComparison.OrdinalIgnoreCase) ||
            (diagnostic ?? "").Contains("load-info-json", StringComparison.OrdinalIgnoreCase);

        public static bool ShouldAutoRetry(YtDlpFailureKind kind, int retryCount, TimeSpan? retryAfter)
        {
            if (kind == YtDlpFailureKind.RateLimited)
                return retryAfter.HasValue && retryCount == 0;

            return retryCount < 3 && kind is YtDlpFailureKind.TemporaryNetwork or YtDlpFailureKind.DnsConnection or
                YtDlpFailureKind.ExpiredMediaUrl;
        }

        public static bool IsTransferring(YtDlpStage stage) =>
            stage is YtDlpStage.Downloading or YtDlpStage.DownloadingAudio or YtDlpStage.DownloadingVideo;

        public static string StageLabel(YtDlpStage stage) => stage switch
        {
            YtDlpStage.Starting => "Starting",
            YtDlpStage.Downloading => "Downloading",
            YtDlpStage.DownloadingVideo => "Downloading video",
            YtDlpStage.DownloadingAudio => "Downloading audio",
            YtDlpStage.Merging => "Merging video and audio",
            YtDlpStage.PostProcessing => "Processing media",
            YtDlpStage.VerifyingOutput => "Checking output file",
            YtDlpStage.Retrying => "Retrying",
            _ => ""
        };

        public static TimeSpan? RetryAfter(string? diagnostic, DateTimeOffset now) =>
            YtDlpDiagnostics.ParseRetryAfter(diagnostic, now);

        public static TimeSpan Backoff(int attempt, double jitterUnit)
        {
            int n = Math.Clamp(attempt, 1, 8);
            double seconds = Math.Min(60, Math.Pow(2, n - 1));
            double jitter = seconds * 0.2 * Math.Clamp(jitterUnit, 0, 1);
            return TimeSpan.FromSeconds(Math.Min(60, seconds + jitter));
        }

        public static async Task WaitBeforeRetryAsync(string? diagnostic, int attempt, CancellationToken ct)
        {
            TimeSpan wait = RetryAfter(diagnostic, DateTimeOffset.UtcNow) ?? Backoff(attempt, Random.Shared.NextDouble());
            await Task.Delay(wait, ct);
        }

        public static IReadOnlyList<string> CopyArguments() => Arguments.ToArray();
    }

    public sealed class YtDlpProcessException : InvalidOperationException
    {
        public int ExitCode { get; }
        public string DiagnosticText { get; }
        public YtDlpFailureKind FailureKind { get; }
        public TimeSpan? RetryAfter { get; }

        public YtDlpProcessException(int exitCode, string diagnosticText)
            : base(ErrorSummary(diagnosticText))
        {
            ExitCode = exitCode;
            DiagnosticText = diagnosticText ?? "";
            FailureKind = YtDlpDiagnostics.Classify(DiagnosticText);
            RetryAfter = YtDlpRetryPolicy.RetryAfter(DiagnosticText, DateTimeOffset.UtcNow);
        }

        static string ErrorSummary(string? diagnostic)
        {
            if (string.IsNullOrWhiteSpace(diagnostic)) return "yt-dlp failed without an error message.";
            string? lastError = diagnostic.Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Substring(6).Trim())
                .LastOrDefault();
            if (string.IsNullOrWhiteSpace(lastError))
                lastError = diagnostic.Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0);
            string safe = AppLog.Redact(lastError ?? "yt-dlp failed.");
            return safe.Length > 400 ? safe.Substring(0, 400) + "…" : safe;
        }
    }
}
