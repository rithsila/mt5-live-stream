using System.Diagnostics;
using System.Text;
using SpikeLogic;

namespace CaptureSpike;

/// <summary>Finds MT5 terminals and builds the (privacy-redacted) window tree.</summary>
internal static class WindowTreeBuilder
{
    /// <summary>All terminal64 processes and their main window handles.</summary>
    public static List<(int Pid, IntPtr Hwnd)> FindMt5Windows()
    {
        var result = new List<(int, IntPtr)>();
        foreach (var p in Process.GetProcessesByName("terminal64"))
        {
            try
            {
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero)
                    result.Add((p.Id, p.MainWindowHandle));
            }
            catch { }
            finally { p.Dispose(); }
        }
        return result;
    }

    /// <summary>
    /// Builds the window tree under a top-level hwnd. Titles are redacted at
    /// collection time — raw window text never enters the model.
    /// NOTE: EnumChildWindows already returns ALL descendants (it recurses on
    /// its own), so the tree is rebuilt from a single flat pass using
    /// GetParent — enumerating per node would add every window twice.
    /// </summary>
    public static WindowNode BuildTree(IntPtr topHwnd)
    {
        var root = NodeFor(topHwnd, isTopLevel: true, depth: 0);
        var byHwnd = new Dictionary<IntPtr, WindowNode> { [topHwnd] = root };
        var orphans = new List<WindowNode>();

        Win32.EnumChildWindows(topHwnd, (h, _) =>
        {
            var node = NodeFor(h, isTopLevel: false, depth: 0);
            byHwnd[h] = node;
            orphans.Add(node);
            return true;
        }, IntPtr.Zero);

        foreach (var node in orphans)
        {
            var parentHwnd = Win32.GetParent(node.Handle);
            if (byHwnd.TryGetValue(parentHwnd, out var parent))
            {
                node.Depth = parent.Depth + 1;
                parent.Children.Add(node);
            }
            else
            {
                // Parent outside our subtree (rare): hang it on the root.
                node.Depth = 1;
                root.Children.Add(node);
            }
        }
        return root;
    }

    private static WindowNode NodeFor(IntPtr hwnd, bool isTopLevel, int depth)
    {
        var cls = new StringBuilder(Win32.MaxPath);
        _ = Win32.GetClassNameW(hwnd, cls, cls.Capacity);

        string? rawTitle = null;
        if (!isTopLevel)
        {
            int len = Win32.GetWindowTextLengthW(hwnd);
            if (len > 0)
            {
                var sb = new StringBuilder(len + 1);
                _ = Win32.GetWindowTextW(hwnd, sb, sb.Capacity);
                rawTitle = sb.ToString();
            }
        }

        _ = Win32.GetWindowRect(hwnd, out var r);

        return new WindowNode
        {
            Handle = hwnd,
            ClassName = cls.ToString(),
            Title = Redaction.SafeTitle(rawTitle, isTopLevel),
            Rect = r.ToRect(),
            Visible = Win32.IsWindowVisible(hwnd),
            Depth = depth,
        };
    }

    /// <summary>Human-readable tree dump for the report (titles already safe).</summary>
    public static List<string> Dump(WindowNode root, ChartPick? pick)
    {
        var lines = new List<string>();
        void Walk(WindowNode n)
        {
            var marker = "";
            if (pick is not null)
            {
                if (n.Handle == pick.ChartWindow?.Handle) marker = "   <== CHART (picked)";
                else if (n.Handle == pick.MdiChild.Handle) marker = "   <== MDI child";
            }
            lines.Add($"{new string(' ', n.Depth * 2)}[{n.ClassName}] rect={n.Rect} visible={n.Visible} title={n.Title}{marker}");
            foreach (var c in n.Children)
                Walk(c);
        }
        Walk(root);
        return lines;
    }
}
