using System;

namespace FastDM
{
    internal readonly record struct UrlValidationResult(int DetectedCount, int NonEmptyLineCount, int FirstInvalidLine);

    internal static class AddUrlValidation
    {
        public static UrlValidationResult Analyze(string? text)
        {
            string[] lines = (text ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            int detected = 0;
            int nonEmpty = 0;
            int firstInvalid = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                nonEmpty++;
                if (IsAllowedLink(line)) detected++;
                else if (firstInvalid == 0) firstInvalid = i + 1;
            }

            return new UrlValidationResult(detected, nonEmpty, firstInvalid);
        }

        public static bool IsAllowedLink(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == Uri.UriSchemeFtp || uri.Scheme == "sftp");
    }
}
