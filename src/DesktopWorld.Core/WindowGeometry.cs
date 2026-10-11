namespace DesktopWorld.Core;

/// <summary>Gameplay-sized frame metrics (physical px). These are NOT the OS metrics: the border is made
/// thick enough to be a walkable platform. Tune via BaseBorder.</summary>
public readonly record struct FrameMetrics(int TitleBarHeight, int BorderThickness)
{
    public const int BaseTitleBar = 32;
    public const int BaseBorder = 6;

    public static FrameMetrics ForDpi(int dpi)
    {
        double s = Math.Max(dpi, 48) / 96.0;
        return new((int)Math.Round(BaseTitleBar * s), Math.Max(1, (int)Math.Round(BaseBorder * s)));
    }
}

/// <summary>A window split into non-overlapping parts that exactly tile <see cref="Outer"/>.
/// Solid: TitleBar, Left, Right, Bottom. Traversable: Interior.</summary>
public sealed record WindowParts(RectI Outer, RectI TitleBar, RectI Left, RectI Right, RectI Bottom, RectI Interior);

public static class WindowGeometry
{
    public static WindowParts Decompose(RectI b, WindowState state, FrameMetrics m)
    {
        if (b.IsEmpty) return new(default, default, default, default, default, default);

        int title = m.TitleBarHeight, side = m.BorderThickness, bottom = m.BorderThickness;
        switch (state)
        {
            case WindowState.Maximized: side = 0; bottom = 0; break;           // frame sits at screen edge
            case WindowState.Fullscreen: title = 0; side = 0; bottom = 0; break; // all interior
        }
        title = Math.Min(title, b.Height);
        side = Math.Min(side, b.Width / 2);
        bottom = Math.Min(bottom, b.Height - title);

        int bodyTop = b.Top + title, bodyBottom = b.Bottom - bottom;
        return new WindowParts(
            Outer: b,
            TitleBar: new(b.Left, b.Top, b.Right, bodyTop),
            Left: new(b.Left, bodyTop, b.Left + side, bodyBottom),
            Right: new(b.Right - side, bodyTop, b.Right, bodyBottom),
            Bottom: new(b.Left, bodyBottom, b.Right, b.Bottom),
            Interior: new(b.Left + side, bodyTop, b.Right - side, bodyBottom));
    }

    public static WindowParts Decompose(WindowInfo w) => Decompose(w.Bounds, w.State, FrameMetrics.ForDpi(w.Dpi));
}
