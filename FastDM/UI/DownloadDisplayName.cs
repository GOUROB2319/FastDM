using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FastDM
{
    /// <summary>Builds a safe, readable filename for UI display without changing the stored name.</summary>
    internal static class DownloadDisplayName
    {
        static readonly HashSet<char> InvalidFilenameCharacters =
            new HashSet<char>(Path.GetInvalidFileNameChars());

        const string WindowsInvalidCharacters = "<>:\"/\\|?*";

        public static string Format(string? value)
        {
            string raw = (value ?? "").Replace('\\', '/');
            string leaf = Path.GetFileName(raw);
            var safe = new StringBuilder(leaf.Length);
            bool previousWasSpace = false;

            foreach (char c in leaf)
            {
                char displayChar = InvalidFilenameCharacters.Contains(c) || WindowsInvalidCharacters.Contains(c)
                    ? ' '
                    : c;

                if (char.IsWhiteSpace(displayChar))
                {
                    if (!previousWasSpace)
                        safe.Append(' ');
                    previousWasSpace = true;
                }
                else
                {
                    safe.Append(displayChar);
                    previousWasSpace = false;
                }
            }

            string display = safe.ToString().Trim().TrimEnd('.', ' ');
            if (display.Length == 0)
                display = "Unnamed download";

            return display.Length > 240 ? display.Substring(0, 237) + "..." : display;
        }
    }
}
