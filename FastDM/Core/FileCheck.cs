#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace FastDM
{
    public static class FileCheck
    {
        // Programs and scripts: opening one from the list runs code, so the user is asked first.
        static readonly HashSet<string> RiskyExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".msi", ".msix", ".appx", ".bat", ".cmd", ".com", ".scr", ".ps1",
            ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta", ".reg", ".lnk", ".jar"
        };

        public static bool IsRiskyToOpen(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                return RiskyExtensions.Contains(Path.GetExtension(path));
            }
            catch
            {
                return false;
            }
        }

        // True only when we are sure the user deleted the file: the drive (or share) is there, the file is not.
        // An unplugged USB drive or a network share that is offline is NOT "deleted".
        public static bool IsDeleted(string? folder, string? fileName)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName))
                return false;

            try
            {
                if (File.Exists(Path.Combine(folder, fileName)))
                    return false;

                string? root = Path.GetPathRoot(Path.GetFullPath(folder));
                return !string.IsNullOrEmpty(root) && Directory.Exists(root);
            }
            catch
            {
                return false;
            }
        }
    }
}
