namespace SpikeLogic;

/// <summary>A pixel rectangle in screen coordinates (X,Y = top-left).</summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public Rect Intersect(Rect other)
    {
        int x = Math.Max(X, other.X);
        int y = Math.Max(Y, other.Y);
        int r = Math.Min(Right, other.Right);
        int b = Math.Min(Bottom, other.Bottom);
        return new Rect(x, y, Math.Max(0, r - x), Math.Max(0, b - y));
    }

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

/// <summary>
/// A window in the MT5 window tree. <see cref="Title"/> is already redacted
/// before it reaches this model — never store raw window text here.
/// </summary>
public sealed class WindowNode
{
    public IntPtr Handle { get; init; }
    public required string ClassName { get; init; }
    public required string Title { get; init; }
    public Rect Rect { get; init; }
    public bool Visible { get; init; }
    public int Depth { get; set; }
    public List<WindowNode> Children { get; } = new();

    public IEnumerable<WindowNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var d in child.Descendants())
                yield return d;
        }
    }
}
