using System;
using System.Collections.Generic;

namespace FastDM
{
    /// <summary>Order in which secondary toolbar labels can be hidden when the toolbar is short.</summary>
    public static class ToolbarLayoutPolicy
    {
        static readonly IReadOnlyList<string> collapseOrder = Array.AsReadOnly(new[]
        {
            "Details",
            "Open File",
            "Open Folder",
            "Settings and updates"
        });

        public static IReadOnlyList<string> CollapseOrder => collapseOrder;

        public static bool MustKeepText(string? toolTip) =>
            string.Equals(toolTip, "Schedule", StringComparison.Ordinal) ||
            string.Equals(toolTip, "After downloads", StringComparison.Ordinal);

        public static bool CanCollapseToIcon(string? toolTip) =>
            !MustKeepText(toolTip);
    }
}
