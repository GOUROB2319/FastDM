using System;

namespace FastDM
{
    internal readonly record struct FolderDownloadFooterLayout(
        int ActionsTop,
        int StatusTop,
        int PlaylistTop,
        int ButtonsTop);

    internal static class FolderDownloadLayoutPolicy
    {
        public static FolderDownloadFooterLayout CalculateFooter(int clientHeight, int dpi = 96)
        {
            int Scale(int logical) => Math.Max(1, (int)Math.Round(logical * dpi / 96.0));
            return new FolderDownloadFooterLayout(
                clientHeight - Scale(136),
                clientHeight - Scale(102),
                clientHeight - Scale(72),
                clientHeight - Scale(42));
        }
    }
}