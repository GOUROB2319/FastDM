using System;
using System.IO;
using System.Linq;

namespace FastDM
{
    public static class FolderNamingPolicy
    {
        public const int MaxFolderNameLength = 80;

        public static string Sanitize(string? value, string fallback = "Downloads")
        {
            string input = (value ?? "").Trim();
            var invalid = Path.GetInvalidFileNameChars();
            string safe = new string(input.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
            safe = string.Join(" ", safe.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.', ' ');
            if (safe.Length > MaxFolderNameLength) safe = safe.Substring(0, MaxFolderNameLength).TrimEnd('.', ' ');
            return safe.Length == 0 ? fallback : safe;
        }

        public static string ListFolder(string filePath) => Sanitize(Path.GetFileNameWithoutExtension(filePath), "Imported links");
        public static string PlaylistFolder(string? title) => Sanitize(title, "Playlist");
        public static string Combine(string baseFolder, string? folderName) => string.IsNullOrWhiteSpace(folderName)
            ? baseFolder.Trim()
            : Path.Combine(baseFolder.Trim(), Sanitize(folderName));

        public static string AppendRelative(string relativeFolder, string segment) =>
            string.IsNullOrWhiteSpace(relativeFolder) ? Sanitize(segment, "_") : Path.Combine(relativeFolder, Sanitize(segment, "_"));
    }
}
