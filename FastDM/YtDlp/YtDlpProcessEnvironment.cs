using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FastDM
{
    public static class YtDlpProcessEnvironment
    {
        public static string BuildRestrictedPath(IEnumerable<string?> directories)
        {
            return string.Join(Path.PathSeparator, directories
                .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                .Select(path => Path.GetFullPath(path!))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }
}
