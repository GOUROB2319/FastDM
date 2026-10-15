using System;
using System.Collections.Generic;

namespace FastDM
{
    public sealed class YtDlpUrlInput
    {
        public IReadOnlyList<string> Arguments { get; }
        public string StandardInput { get; }

        YtDlpUrlInput(IReadOnlyList<string> arguments, string standardInput)
        {
            Arguments = arguments;
            StandardInput = standardInput;
        }

        public static YtDlpUrlInput Create(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || url.Length > 8192 || url.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("The video URL is invalid.", nameof(url));
            return new YtDlpUrlInput(new[] { "--batch-file", "-" }, url + "\n");
        }
    }
}
