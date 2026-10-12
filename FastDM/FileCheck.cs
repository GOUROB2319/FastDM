#nullable enable
using System;
using System.IO;

namespace FastDM
{
    public static class FileCheck
    {
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
