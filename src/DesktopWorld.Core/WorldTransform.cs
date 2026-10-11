namespace DesktopWorld.Core;

public readonly record struct Vec2(double X, double Y);
public readonly record struct RectD(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
}

/// <summary>
/// Screen-space (physical pixels, virtual screen) &lt;-&gt; world-space (Terraria "world pixels", 16 per tile, +Y down).
///   world = (screen - ScreenOrigin) / PixelsPerWorldUnit
/// The process must be Per-Monitor-V2 DPI aware so screen coordinates really are physical pixels.
/// PixelsPerWorldUnit defaults to primaryDpi/96 so that Terraria's 48px-tall player appears at a
/// consistent logical size (e.g. 1.5 at 150% scaling). ScreenOrigin doubles as the camera offset.
/// </summary>
public sealed class WorldTransform
{
    public const double TileSize = 16.0;

    public Vec2 ScreenOrigin { get; }
    public double PixelsPerWorldUnit { get; }

    public WorldTransform(Vec2 screenOrigin, double pixelsPerWorldUnit)
    {
        if (!(pixelsPerWorldUnit > 0) || double.IsInfinity(pixelsPerWorldUnit))
            throw new ArgumentOutOfRangeException(nameof(pixelsPerWorldUnit));
        ScreenOrigin = screenOrigin;
        PixelsPerWorldUnit = pixelsPerWorldUnit;
    }

    public static WorldTransform ForDesktop(RectI virtualScreen, int primaryDpi) =>
        new(new Vec2(virtualScreen.Left, virtualScreen.Top), Math.Max(primaryDpi, 48) / 96.0);

    public WorldTransform WithScreenOrigin(Vec2 origin) => new(origin, PixelsPerWorldUnit);

    public Vec2 ScreenToWorld(Vec2 s) => new((s.X - ScreenOrigin.X) / PixelsPerWorldUnit, (s.Y - ScreenOrigin.Y) / PixelsPerWorldUnit);
    public Vec2 WorldToScreen(Vec2 w) => new(w.X * PixelsPerWorldUnit + ScreenOrigin.X, w.Y * PixelsPerWorldUnit + ScreenOrigin.Y);

    public RectD ToWorld(RectI r)
    {
        var a = ScreenToWorld(new(r.Left, r.Top));
        var b = ScreenToWorld(new(r.Right, r.Bottom));
        return new(a.X, a.Y, b.X, b.Y);
    }

    public RectI ToScreen(RectD r)
    {
        var a = WorldToScreen(new(r.Left, r.Top));
        var b = WorldToScreen(new(r.Right, r.Bottom));
        return new((int)Math.Round(a.X), (int)Math.Round(a.Y), (int)Math.Round(b.X), (int)Math.Round(b.Y));
    }

    /// <summary>Tile containing a world point (floor, so negative coordinates behave).</summary>
    public static (int X, int Y) WorldToTile(Vec2 w) => ((int)Math.Floor(w.X / TileSize), (int)Math.Floor(w.Y / TileSize));
    public static Vec2 TileToWorld(int tx, int ty) => new(tx * TileSize, ty * TileSize);
}
