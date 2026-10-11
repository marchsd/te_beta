namespace DesktopWorld.Core;

public enum WindowState { Normal, Maximized, Fullscreen }

/// <summary>One top-level window. Deliberately contains NO title, text, or content: geometry only.
/// <see cref="Id"/> is the HWND value and is stable for the window's lifetime (reusable after it closes).</summary>
public sealed record WindowInfo(
    long Id,
    int ProcessId,
    RectI Bounds,        // visible frame (DWM extended frame bounds), physical px
    WindowState State,
    int ZOrder,          // 0 = topmost
    int MonitorIndex,
    int Dpi);

public sealed record MonitorInfo(int Index, RectI Bounds, RectI WorkArea, int Dpi, bool IsPrimary);

public sealed record DesktopSnapshot(
    IReadOnlyList<MonitorInfo> Monitors,
    IReadOnlyList<WindowInfo> Windows,   // topmost first
    RectI Taskbar,                       // empty if hidden / not detected
    long TimestampMs)
{
    public RectI VirtualScreen => Monitors.Aggregate(default(RectI), (acc, m) => acc.Union(m.Bounds));
    public MonitorInfo? Primary => Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();
}

public interface IDesktopSource
{
    DesktopSnapshot Capture();
}
