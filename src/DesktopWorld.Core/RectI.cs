namespace DesktopWorld.Core;

/// <summary>Integer rectangle in PHYSICAL screen pixels (virtual-screen coordinates; may be negative on
/// multi-monitor setups). Half-open: Left/Top inclusive, Right/Bottom exclusive.</summary>
public readonly record struct RectI(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public static RectI FromXYWH(int x, int y, int w, int h) => new(x, y, x + w, y + h);

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public bool IntersectsWith(RectI o) => Left < o.Right && o.Left < Right && Top < o.Bottom && o.Top < Bottom;

    public RectI Intersect(RectI o)
    {
        var r = new RectI(Math.Max(Left, o.Left), Math.Max(Top, o.Top), Math.Min(Right, o.Right), Math.Min(Bottom, o.Bottom));
        return r.IsEmpty ? default : r;
    }

    public RectI Union(RectI o)
    {
        if (IsEmpty) return o;
        if (o.IsEmpty) return this;
        return new(Math.Min(Left, o.Left), Math.Min(Top, o.Top), Math.Max(Right, o.Right), Math.Max(Bottom, o.Bottom));
    }

    public override string ToString() => $"({Left},{Top} {Width}x{Height})";
}
