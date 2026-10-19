#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FastDM
{
    // Helpers for downloads whose file name comes from the video title.
    // yt-dlp works in a private folder per download (.fastdm-<id>); when it finishes, FastDM finds the
    // finished file there and moves it to the real folder. The file name never has to travel through
    // yt-dlp's text output, so titles in any language are safe.
    public static class YtDlpOutput
    {
        // Half-finished files that are not the result
        public static readonly Regex Intermediate = new Regex(@"\.f\d+(\.|$)|\.part$|\.ytdl$|\.temp$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ItemDirName(string itemId) =>
            ".fastdm-" + (itemId.Length >= 8 ? itemId.Substring(0, 8) : itemId);


        // The newest finished file in the folder, or null.
        public static string? FindFinal(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;

                return Directory.GetFiles(dir)
                    .Where(f => !Intermediate.IsMatch(Path.GetFileName(f)) && new FileInfo(f).Length > 0)
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        // Moves the file into targetFolder; if the name is taken it becomes "name (1).ext", "name (2).ext", ...
        public static string MoveToUnique(string file, string targetFolder)
        {
            Directory.CreateDirectory(targetFolder);

            string name = Path.GetFileName(file);
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);

            string dest = Path.Combine(targetFolder, name);
            for (int i = 1; File.Exists(dest); i++)
                dest = Path.Combine(targetFolder, stem + " (" + i + ")" + ext);

            File.Move(file, dest);
            return dest;
        }
    }
}
