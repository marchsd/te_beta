using System.Diagnostics;

namespace DesktopWorld.Core;

/// <summary>Deterministic fake desktop for development without Windows/Terraria. Windows 1001..1004:
/// a static "browser", a moving "editor", a resizing "terminal", and a "dialog" that opens/closes every 5 s.</summary>
public sealed class MockDesktopSource : IDesktopSource
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    public DesktopSnapshot Capture() => CaptureAt(_clock.ElapsedMilliseconds);

    public DesktopSnapshot CaptureAt(long ms)
    {
        var monitor = new MonitorInfo(0, RectI.FromXYWH(0, 0, 1920, 1080), RectI.FromXYWH(0, 0, 1920, 1040), 96, true);
        var list = new List<WindowInfo>();
        if ((ms / 5000) % 2 == 1)
            list.Add(new(1004, 4, RectI.FromXYWH(500, 400, 400, 250), WindowState.Normal, 0, 0, 96));

        int editorX = 700 + (int)(200 * Math.Sin(ms / 2000.0));
        list.Add(new(1002, 2, RectI.FromXYWH(editorX, 200, 700, 500), WindowState.Normal, 0, 0, 96));
        int termW = 500 + (int)(150 * Math.Sin(ms / 3000.0));
        list.Add(new(1003, 3, RectI.FromXYWH(1200, 100, termW, 450), WindowState.Normal, 0, 0, 96));
        list.Add(new(1001, 1, RectI.FromXYWH(100, 80, 900, 600), WindowState.Normal, 0, 0, 96));

        var ordered = list.Select((w, i) => w with { ZOrder = i }).ToList();
        return new DesktopSnapshot(new[] { monitor }, ordered, RectI.FromXYWH(0, 1040, 1920, 40), ms);
    }
}
