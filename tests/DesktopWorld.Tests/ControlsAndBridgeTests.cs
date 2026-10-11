using DesktopWorld.Bridge;
using DesktopWorld.Core;
using DesktopWorld.Core.World;
using DesktopWorld.Protocol;

namespace DesktopWorld.Tests;

public class ControlsTests
{
    [Fact] public void DefaultsMatchSpec()
    {
        var c = new ControlsConfig();
        Assert.Equal((int)'A', c.Left); Assert.Equal((int)'D', c.Right); Assert.Equal(0x20, c.Jump);
        Assert.Equal((int)'W', c.Up); Assert.Equal((int)'S', c.Down); Assert.Equal(0x1B, c.Leave);
    }
    [Fact] public void ParsesRebindsAndReportsProblems()
    {
        var c = ControlsConfig.Parse("left = Left\nright=right\n# comment\njump = F5 # trailing\nfly = Z\nattack = nokey\nmalformed\n", out var problems);
        Assert.Equal(0x25, c.Left); Assert.Equal(0x27, c.Right); Assert.Equal(0x74, c.Jump);
        Assert.Equal(3, problems.Count(p => !p.StartsWith("warning")));
    }
    [Fact] public void WarnsOnDuplicateBindings()
    {
        ControlsConfig.Parse("jump = W\n", out var problems);
        Assert.True(problems.Any(p => p.StartsWith("warning") && p.Contains("jump")));
    }
    [Fact] public void IniRoundTrips()
    {
        var a = ControlsConfig.Parse("left = J\nmine = MMB\n", out _);
        var b = ControlsConfig.Parse(a.ToIni(), out var problems);
        Assert.Equal(a.Left, b.Left); Assert.Equal(a.Mine, b.Mine); Assert.Equal(0, problems.Count);
    }
}

public class CompanionBridgeTests
{
    [Fact] public void GeometryMessageMirrorsWorldModel()
    {
        var rig = new WorldHarness.Rig();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, WorldHarness.W(1, 100, 300, 600, 400, 0)));
        var tf = WorldTransform.ForDesktop(rig.Model.VirtualScreen, 144);
        var bridge = new CompanionBridge(new PipeServer("t", "unused"), rig.Model, () => tf);
        var g = bridge.BuildGeometry();
        Assert.Equal(rig.Model.CollisionRects.Count, g.Solids.Count);
        Assert.Equal(rig.Model.Interiors.Count, g.Interiors.Count);
        Assert.Near(1.5, g.PixelsPerWorldUnit);
        Assert.True(Codec.TryDecode(Codec.Encode(g), out var back, out var err), err);   // fits the wire format
    }

    [Fact] public void ModMineEventEditsSimulatedTerrainAndPushesGeometry() => Task.Run(async () =>
    {
        var name = "dw-test-" + Guid.NewGuid().ToString("N");
        var rig = new WorldHarness.Rig();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, WorldHarness.W(1, 100, 300, 600, 400, 0)));
        var tf = WorldTransform.ForDesktop(rig.Model.VirtualScreen, 96);
        using var cts = new CancellationTokenSource();
        var server = new PipeServer("t", name); var bridge = new CompanionBridge(server, rig.Model, () => tf);
        var client = new PipeClient("mod", "t", name);
        var got = new List<Msg>(); client.Received += m => { lock (got) got.Add(m); };
        var st = server.RunAsync(cts.Token); var ct = client.RunAsync(cts.Token);

        async Task<bool> Wait(Func<bool> c) { for (int i = 0; i < 250 && !c(); i++) await Task.Delay(20); return c(); }
        Assert.True(await Wait(() => { lock (got) return got.OfType<GeometryMsg>().Any(); }), "geometry sent on connect");
        Assert.True(await Wait(() => { lock (got) return got.OfType<ModeMsg>().Any(); }), "mode sent on connect");
        int before; lock (got) before = got.OfType<GeometryMsg>().Count();

        client.Send(new MineMsg { X = 300, Y = 310 });
        Assert.True(await Wait(() => { lock (got) return got.OfType<GeometryMsg>().Count() > before; }), "geometry re-sent after mining");
        Assert.Equal(Surface.Open, rig.Model.Classify(300, 310));
        cts.Cancel(); try { await Task.WhenAll(st, ct); } catch { }
    }).GetAwaiter().GetResult();
}
