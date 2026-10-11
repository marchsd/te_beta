using DesktopWorld.Core;
using DesktopWorld.Core.World;

namespace DesktopWorld.Tests;

internal static class WorldHarness
{
    public static WindowInfo W(long id, int x, int y, int w, int h, int z, WindowState s = WindowState.Normal) =>
        new(id, 1, RectI.FromXYWH(x, y, w, h), s, z, 0, 96);

    public static DesktopSnapshot Snap(long ts, RectI taskbar, params WindowInfo[] ws) =>
        new(new[] { new MonitorInfo(0, RectI.FromXYWH(0, 0, 1920, 1080), RectI.FromXYWH(0, 0, 1920, 1040), 96, true) }, ws, taskbar, ts);

    public static readonly RectI Taskbar = RectI.FromXYWH(0, 1040, 1920, 40);

    public sealed class Rig
    {
        public readonly WindowTracker Tracker = new();
        public readonly WorldModel Model;
        public Rig(WorldModelOptions? o = null) => Model = new(o);
        public bool Feed(DesktopSnapshot s) => Model.Apply(s, Tracker.Update(s));
    }
}

public class WorldModelTests
{
    static readonly Func<long, int, int, int, int, int, WindowInfo> W = (id, x, y, w, h, z) => WorldHarness.W(id, x, y, w, h, z);

    [Fact] public void RectSubtractCoversExactly()
    {
        var a = RectI.FromXYWH(0, 0, 100, 100); var b = RectI.FromXYWH(30, 40, 20, 20);
        var list = new List<RectI>(); RectOps.Subtract(a, b, list);
        Assert.Equal(a.Area - b.Area, list.Sum(r => r.Area));
        for (int i = 0; i < list.Count; i++) for (int j = i + 1; j < list.Count; j++) Assert.True(!list[i].IntersectsWith(list[j]));
        Assert.True(list.All(r => !r.IntersectsWith(b)));
    }

    [Fact] public void ClassifiesPartsOfASingleWindow()
    {
        var rig = new WorldHarness.Rig();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, W(1, 100, 100, 600, 400, 0)));
        var m = rig.Model;
        Assert.Equal(Surface.Solid, m.Classify(300, 110));      // title bar
        Assert.Equal(Surface.Solid, m.Classify(102, 300));      // left border
        Assert.Equal(Surface.Interior, m.Classify(300, 300));
        Assert.Equal(Surface.Open, m.Classify(50, 50));
        Assert.Equal(Surface.Solid, m.Classify(300, 1050));     // taskbar
    }

    [Fact] public void HigherWindowOccludesLowerOnesBorders()
    {
        var rig = new WorldHarness.Rig();
        // lower window title bar at y=100..132; upper window covers x 300..700,y 50..300
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, W(2, 300, 50, 400, 250, 0), W(1, 100, 100, 600, 400, 1)));
        Assert.Equal(Surface.Solid, rig.Model.Classify(150, 110));        // visible part of lower title bar
        Assert.Equal(Surface.Solid, rig.Model.Classify(400, 60));         // upper title bar
        Assert.Equal(Surface.Interior, rig.Model.Classify(400, 200));     // upper interior wins
        Assert.True(!rig.Model.Pieces.Any(p => p.OwnerId == 1 && p.Rect.IntersectsWith(RectI.FromXYWH(300, 50, 400, 250))));
    }

    [Fact] public void MiningCreatesGapAndOpensInterior()
    {
        var rig = new WorldHarness.Rig();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, W(1, 100, 100, 600, 400, 0)));
        var r = rig.Model.TryMine(300, 110);
        Assert.Equal(MineOutcome.Mined, r.Outcome);
        Assert.Equal(Surface.Open, rig.Model.Classify(300, 110));
        Assert.Equal(Surface.Solid, rig.Model.Classify(300 + 40, 110));  // neighbour segments intact
        Assert.Equal(1, rig.Model.MinedCount(1));
    }

    [Fact] public void TaskbarAndFloorAreUnmineable()
    {
        var rig = new WorldHarness.Rig();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar));
        Assert.Equal(MineOutcome.Unmineable, rig.Model.TryMine(500, 1050).Outcome);
        Assert.Equal(MineOutcome.Nothing, rig.Model.TryMine(500, 500).Outcome);
        var noBar = new WorldHarness.Rig(); noBar.Feed(WorldHarness.Snap(0, default));
        Assert.Equal(Surface.Solid, noBar.Model.Classify(500, 1075));   // floor
        Assert.Equal(MineOutcome.Unmineable, noBar.Model.TryMine(500, 1075).Outcome);
    }

    [Fact] public void HolesFollowWindowMove()
    {
        var rig = new WorldHarness.Rig();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, W(1, 100, 100, 600, 400, 0)));
        rig.Model.TryMine(300, 110);                       // title bar segment index (300-100)/16 = 12
        rig.Feed(WorldHarness.Snap(50, WorldHarness.Taskbar, W(1, 400, 200, 600, 400, 0)));
        Assert.Equal(Surface.Open, rig.Model.Classify(400 + 12 * 16 + 4, 210));  // hole moved with window
        Assert.Equal(Surface.Solid, rig.Model.Classify(400 + 13 * 16 + 4, 210));   // neighbouring segment intact
    }

    [Fact] public void HolesStayOnTheirBorderAfterResizeAndAreRememberedWhenClipped()
    {
        var rig = new WorldHarness.Rig();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, W(1, 100, 100, 600, 400, 0)));
        // mine right border segment 5 (counted from top of the strip: y = 132 + 5*16..)
        int y = 132 + 5 * 16 + 4;
        Assert.Equal(MineOutcome.Mined, rig.Model.TryMine(100 + 600 - 3, y).Outcome);
        rig.Feed(WorldHarness.Snap(10, WorldHarness.Taskbar, W(1, 100, 100, 900, 400, 0)));   // widen
        Assert.Equal(Surface.Open, rig.Model.Classify(100 + 900 - 3, y));
        // shrink so the strip is shorter than segment 5; hole inactive but remembered
        rig.Feed(WorldHarness.Snap(20, WorldHarness.Taskbar, W(1, 100, 100, 900, 150, 0)));
        Assert.Equal(1, rig.Model.MinedCount(1));
        rig.Feed(WorldHarness.Snap(30, WorldHarness.Taskbar, W(1, 100, 100, 900, 400, 0)));
        Assert.Equal(Surface.Open, rig.Model.Classify(100 + 900 - 3, y));
    }

    [Fact] public void TombstoneRestoresMinedTerrainWithinGrace_ExpiresAfter()
    {
        var rig = new WorldHarness.Rig(new WorldModelOptions { TombstoneGrace = TimeSpan.FromSeconds(5) });
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, W(1, 100, 100, 600, 400, 0)));
        rig.Model.TryMine(300, 110);
        rig.Feed(WorldHarness.Snap(1000, WorldHarness.Taskbar));                    // minimized/closed
        Assert.Equal(1, rig.Model.TombstoneCount);
        Assert.Equal(Surface.Open, rig.Model.Classify(300, 200));
        rig.Feed(WorldHarness.Snap(3000, WorldHarness.Taskbar, W(1, 100, 100, 600, 400, 0)));   // restored in time
        Assert.Equal(Surface.Open, rig.Model.Classify(300, 110));
        rig.Feed(WorldHarness.Snap(4000, WorldHarness.Taskbar));
        rig.Feed(WorldHarness.Snap(20000, WorldHarness.Taskbar));                   // grace expired
        Assert.Equal(0, rig.Model.TombstoneCount);
        rig.Feed(WorldHarness.Snap(21000, WorldHarness.Taskbar, W(1, 100, 100, 600, 400, 0)));
        Assert.Equal(Surface.Solid, rig.Model.Classify(300, 110));                  // fresh window, hole gone
    }

    [Fact] public void NoChangeNoRebuild_AndOnlyChangedWindowRecomputesParts()
    {
        var rig = new WorldHarness.Rig();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, W(1, 100, 100, 300, 300, 0), W(2, 600, 100, 300, 300, 1)));
        int rebuilds = rig.Model.RebuildCount, parts = rig.Model.PartsRecomputed;
        Assert.True(!rig.Feed(WorldHarness.Snap(16, WorldHarness.Taskbar, W(1, 100, 100, 300, 300, 0), W(2, 600, 100, 300, 300, 1))));
        Assert.Equal(rebuilds, rig.Model.RebuildCount);
        rig.Feed(WorldHarness.Snap(32, WorldHarness.Taskbar, W(1, 120, 100, 300, 300, 0), W(2, 600, 100, 300, 300, 1)));
        Assert.Equal(parts + 1, rig.Model.PartsRecomputed);
    }

    [Fact] public void FullscreenWindowIsOccluderOnly()
    {
        var rig = new WorldHarness.Rig();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar,
            WorldHarness.W(9, 0, 0, 1920, 1080, 0, WindowState.Fullscreen), W(1, 100, 100, 600, 400, 1)));
        Assert.Equal(Surface.Open, rig.Model.Classify(300, 110));
        Assert.Equal(0, rig.Model.Pieces.Count);
    }

    [Fact] public void MergedCollisionHasNoSeamsAndSameCoverage()
    {
        var rig = new WorldHarness.Rig();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, W(1, 100, 100, 600, 400, 0)));
        Assert.True(rig.Model.CollisionRects.Count < rig.Model.Pieces.Count);
        Assert.Equal(rig.Model.Pieces.Sum(p => p.Rect.Area), rig.Model.CollisionRects.Sum(r => r.Area));
    }

    [Fact] public void MockDesktopDrivesWorldThroughDialogLifecycle()
    {
        var mock = new MockDesktopSource(); var rig = new WorldHarness.Rig();
        foreach (long t in new long[] { 0, 1000, 6000, 11000, 16000 }) rig.Feed(mock.CaptureAt(t));
        Assert.True(rig.Model.Version > 1);
    }
}
