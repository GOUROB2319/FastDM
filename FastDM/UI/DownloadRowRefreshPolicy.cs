namespace FastDM
{
    internal static class DownloadRowRefreshPolicy
    {
        public static bool CellChanged(string? previous, string? next) =>
            !string.Equals(previous, next, System.StringComparison.Ordinal);

        public static bool ShouldRedraw(bool anyCellChanged, object? previousProgressFillWidth, int progressFillWidth) =>
            anyCellChanged || !Equals(previousProgressFillWidth, progressFillWidth);
    }
}
