using DesktopWorld.Core;

namespace DesktopWorld.Tests;

public class RectTests
{
    [Fact] public void IntersectAndContains()
    {
        var a = RectI.FromXYWH(0, 0, 10, 10); var b = RectI.FromXYWH(5, 5, 10, 10);
        Assert.Equal(RectI.FromXYWH(5, 5, 5, 5), a.Intersect(b));
        Assert.True(a.Contains(0, 0)); Assert.True(!a.Contains(10, 10));
        Assert.True(a.Intersect(RectI.FromXYWH(10, 0, 5, 5)).IsEmpty, "touching edges do not intersect");
    }
    [Fact] public void UnionIgnoresEmpty() => Assert.Equal(RectI.FromXYWH(1, 1, 2, 2), default(RectI).Union(RectI.FromXYWH(1, 1, 2, 2)));
}

public class GeometryTests
{
    static readonly FrameMetrics M = new(32, 6);

    static void AssertTiles(WindowParts p)
    {
        long sum = p.TitleBar.Area + p.Left.Area + p.Right.Area + p.Bottom.Area + p.Interior.Area;
        Assert.Equal(p.Outer.Area, sum);
        var all = new[] { p.TitleBar, p.Left, p.Right, p.Bottom, p.Interior };
        for (int i = 0; i < all.Length; i++) for (int j = i + 1; j < all.Length; j++)
            Assert.True(!all[i].IntersectsWith(all[j]), $"parts {i} and {j} overlap");
    }

    [Fact] public void NormalWindowTilesExactly()
    {
        var p = WindowGeometry.Decompose(RectI.FromXYWH(100, 50, 800, 600), WindowState.Normal, M);
        AssertTiles(p);
        Assert.Equal(RectI.FromXYWH(106, 82, 788, 562), p.Interior);
        Assert.Equal(32, p.TitleBar.Height);
    }
    [Fact] public void TinyWindowsClampAndStillTile()
    {
        foreach (var (w, h) in new[] { (10, 10), (3, 100), (100, 20), (1, 1) })
            AssertTiles(WindowGeometry.Decompose(RectI.FromXYWH(-5, -5, w, h), WindowState.Normal, M));
    }
    [Fact] public void MaximizedHasNoSideBorders()
    {
        var p = WindowGeometry.Decompose(RectI.FromXYWH(0, 0, 1920, 1040), WindowState.Maximized, M);
        AssertTiles(p); Assert.True(p.Left.IsEmpty && p.Right.IsEmpty && p.Bottom.IsEmpty);
    }
    [Fact] public void FullscreenIsAllInterior()
    {
        var b = RectI.FromXYWH(0, 0, 1920, 1080);
        var p = WindowGeometry.Decompose(b, WindowState.Fullscreen, M);
        Assert.Equal(b, p.Interior);
    }
    [Fact] public void MetricsScaleWithDpi()
    {
        Assert.Equal(new FrameMetrics(32, 6), FrameMetrics.ForDpi(96));
        Assert.Equal(new FrameMetrics(48, 9), FrameMetrics.ForDpi(144));
    }
    [Fact] public void NegativeCoordinatesWork()
    {
        var p = WindowGeometry.Decompose(RectI.FromXYWH(-1900, 100, 800, 600), WindowState.Normal, M);
        AssertTiles(p); Assert.Equal(-1900, p.Left.Left);
    }
}

public class TransformTests
{
    [Fact] public void RoundTrip()
    {
        var t = new WorldTransform(new Vec2(-1920, 0), 1.5);
        var s = new Vec2(-500.5, 321.25);
        var back = t.WorldToScreen(t.ScreenToWorld(s));
        Assert.Near(s.X, back.X); Assert.Near(s.Y, back.Y);
    }
    [Fact] public void DpiScaleMakesWorldSmaller()
    {
        var t = WorldTransform.ForDesktop(RectI.FromXYWH(0, 0, 2880, 1620), 144); // 150%
        var w = t.ToWorld(RectI.FromXYWH(0, 0, 2880, 1620));
        Assert.Near(1920, w.Width); Assert.Near(1080, w.Height);
    }
    [Fact] public void OriginFollowsLeftMonitor()
    {
        var t = WorldTransform.ForDesktop(new RectI(-1920, 0, 1920, 1080), 96);
        var w = t.ScreenToWorld(new Vec2(-1920, 0));
        Assert.Near(0, w.X);
    }
    [Fact] public void TilesFloorForNegatives()
    {
        Assert.Equal((-1, 0), WorldTransform.WorldToTile(new Vec2(-0.1, 15.9)));
        Assert.Equal((2, -2), WorldTransform.WorldToTile(new Vec2(32, -17)));
    }
    [Fact] public void RectScreenRoundTrip()
    {
        var t = WorldTransform.ForDesktop(RectI.FromXYWH(0, 0, 1920, 1080), 120);
        var r = RectI.FromXYWH(130, 70, 640, 480);
        Assert.Equal(r, t.ToScreen(t.ToWorld(r)));
    }
    [Fact] public void RejectsBadScale()
    {
        try { _ = new WorldTransform(default, 0); Assert.True(false, "should throw"); } catch (ArgumentOutOfRangeException) { }
    }
}

public class TrackerTests
{
    static WindowInfo W(long id, int x, int y, int w, int h, int z, WindowState s = WindowState.Normal) =>
        new(id, 1, RectI.FromXYWH(x, y, w, h), s, z, 0, 96);
    static DesktopSnapshot S(params WindowInfo[] ws) =>
        new(new[] { new MonitorInfo(0, RectI.FromXYWH(0, 0, 1920, 1080), RectI.FromXYWH(0, 0, 1920, 1040), 96, true) }, ws, default, 0);

    [Fact] public void AddedThenNoChange()
    {
        var t = new WindowTracker();
        var c = t.Update(S(W(1, 0, 0, 100, 100, 0)));
        Assert.Equal(WindowChangeKind.Added, c.Single().Kind);
        Assert.Equal(0, t.Update(S(W(1, 0, 0, 100, 100, 0))).Count);
    }
    [Fact] public void MovedResizedRemoved()
    {
        var t = new WindowTracker(); t.Update(S(W(1, 0, 0, 100, 100, 0)));
        Assert.Equal(WindowChangeKind.Moved, t.Update(S(W(1, 10, 0, 100, 100, 0))).Single().Kind);
        Assert.Equal(WindowChangeKind.Resized, t.Update(S(W(1, 10, 0, 120, 100, 0))).Single().Kind);
        Assert.Equal(WindowChangeKind.Resized, t.Update(S(W(1, 5, 0, 130, 100, 0))).Single().Kind); // resize from left edge moves too
        Assert.Equal(WindowChangeKind.Removed, t.Update(S()).Single().Kind);
    }
    [Fact] public void StateChange()
    {
        var t = new WindowTracker(); t.Update(S(W(1, 0, 0, 100, 100, 0)));
        var c = t.Update(S(W(1, 0, 0, 1920, 1040, 0, WindowState.Maximized)));
        Assert.True(c.Any(x => x.Kind == WindowChangeKind.StateChanged));
    }
    [Fact] public void ZOrderSwapReported_AddRemoveAloneIsNot()
    {
        var t = new WindowTracker(); t.Update(S(W(1, 0, 0, 10, 10, 0), W(2, 50, 0, 10, 10, 1)));
        var swap = t.Update(S(W(2, 50, 0, 10, 10, 0), W(1, 0, 0, 10, 10, 1)));
        Assert.Equal(2, swap.Count(x => x.Kind == WindowChangeKind.ZOrderChanged));
        // new window on top shifts indices but not relative order of survivors
        var add = t.Update(S(W(3, 0, 0, 10, 10, 0), W(2, 50, 0, 10, 10, 1), W(1, 0, 0, 10, 10, 2)));
        Assert.True(add.All(x => x.Kind == WindowChangeKind.Added));
    }
    [Fact] public void IdReuseAfterRemovalIsAdded()
    {
        var t = new WindowTracker(); t.Update(S(W(1, 0, 0, 10, 10, 0))); t.Update(S());
        Assert.Equal(WindowChangeKind.Added, t.Update(S(W(1, 0, 0, 10, 10, 0))).Single().Kind);
    }
}

public class MockAndMapTests
{
    [Fact] public void MockIsDeterministic()
    {
        var m = new MockDesktopSource();
        Assert.Equal(m.CaptureAt(1234).Windows.Count, m.CaptureAt(1234).Windows.Count);
        Assert.Equal(m.CaptureAt(1234).Windows[0].Bounds, m.CaptureAt(1234).Windows[0].Bounds);
    }
    [Fact] public void MockDialogOpensAndCloses()
    {
        var m = new MockDesktopSource();
        Assert.Equal(3, m.CaptureAt(1000).Windows.Count);
        Assert.Equal(4, m.CaptureAt(6000).Windows.Count);
        Assert.Equal(3, m.CaptureAt(11000).Windows.Count);
    }
    [Fact] public void TrackerSeesMockDialogLifecycle()
    {
        var m = new MockDesktopSource(); var t = new WindowTracker(); t.Update(m.CaptureAt(1000));
        Assert.True(t.Update(m.CaptureAt(6000)).Any(c => c.Kind == WindowChangeKind.Added && c.Id == 1004));
        Assert.True(t.Update(m.CaptureAt(11000)).Any(c => c.Kind == WindowChangeKind.Removed && c.Id == 1004));
    }
    [Fact] public void AsciiMapTopmostWinsAndTaskbarShown()
    {
        var snap = new MockDesktopSource().CaptureAt(6000);
        var map = AsciiMap.Render(snap, 96, 54);
        Assert.True(map.Contains('=') && map.Contains('#') && map.Contains('.') && map.Contains('T'));
        Assert.Equal(54, map.Count(ch => ch == '\n'));
    }
    [Fact] public void WatcherRaisesMonitorsChangedOnceAndUpdates()
    {
        int monitors = 0, updates = 0;
        var w = new DesktopWatcher(new MockDesktopSource(), TimeSpan.FromMilliseconds(10));
        w.MonitorsChanged += _ => monitors++; w.Updated += (_, _) => updates++;
        w.PollOnce(); w.PollOnce();
        Assert.Equal(1, monitors); Assert.Equal(2, updates);
    }
}
