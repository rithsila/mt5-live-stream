namespace SpikeLogic;

public sealed record ChartPick(
    WindowNode MdiChild,
    WindowNode? ChartWindow,
    Rect ChartScreenRect);

/// <summary>
/// Picks the chart rectangle out of an MT5 window tree:
/// the largest visible child of the "MDIClient" window, then the largest
/// visible descendant inside that child (fallback: the child's own rect).
/// </summary>
public static class ChartPicker
{
    public const string MdiClientClassName = "MDIClient";

    /// <summary>Chart pick by index into the MDIClient's visible children (sorted by area desc). Null = auto (largest).</summary>
    public static ChartPick? PickChart(WindowNode topLevel, int? childIndex = null)
    {
        var mdi = FindClass(topLevel, MdiClientClassName);
        if (!topLevel.Visible || mdi is null || !mdi.Visible)
            return null;

        var visibleChildren = mdi.Children
            .Where(c => c.Visible && c.Rect.Area > 0)
            .OrderByDescending(c => c.Rect.Area)
            .ToList();
        if (visibleChildren.Count == 0)
            return null;

        WindowNode mdiChild;
        if (childIndex is null)
        {
            mdiChild = visibleChildren[0];
        }
        else
        {
            if (childIndex.Value < 0 || childIndex.Value >= visibleChildren.Count)
                return null;
            mdiChild = visibleChildren[childIndex.Value];
        }

        // Largest visible descendant of the MDI child = the chart canvas.
        var chartWindow = mdiChild
            .Descendants()
            .Where(d => d.Visible && d.Rect.Area > 0)
            .OrderByDescending(d => d.Rect.Area)
            .FirstOrDefault();

        var rect = chartWindow?.Rect ?? mdiChild.Rect;
        if (rect.Intersect(mdi.Rect) != rect || rect.Intersect(topLevel.Rect) != rect ||
            rect.Intersect(mdiChild.Rect) != rect)
            return null;
        return new ChartPick(mdiChild, chartWindow, rect);
    }

    private static WindowNode? FindClass(WindowNode node, string className)
    {
        if (string.Equals(node.ClassName, className, StringComparison.OrdinalIgnoreCase))
            return node;
        foreach (var child in node.Children)
        {
            var found = FindClass(child, className);
            if (found is not null)
                return found;
        }
        return null;
    }
}
