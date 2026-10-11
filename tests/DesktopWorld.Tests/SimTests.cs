using DesktopWorld.Core;
using DesktopWorld.Core.Sim;
using DesktopWorld.Core.World;

namespace DesktopWorld.Tests;

public class SimTests
{
    // Window: x 100..700, y 300..700. Title bar y300..332, borders 6 px, bottom border y694..700. Taskbar y>=1040.
    static (WorldModel m, Simulation s, WorldHarness.Rig rig) Setup(params WindowInfo[] extra)
    {
        var rig = new WorldHarness.Rig();
        var wins = new[] { WorldHarness.W(1, 100, 300, 600, 400, 0) }.Concat(extra).Select((w, i) => w with { ZOrder = i }).ToArray();
        rig.Feed(WorldHarness.Snap(0, WorldHarness.Taskbar, wins));
        var sim = new Simulation(rig.Model, WorldTransform.ForDesktop(rig.Model.VirtualScreen, 96));
        return (rig.Model, sim, rig);
    }

    static void Run(Simulation s, int ticks, PlayerInput inp) { for (int i = 0; i < ticks; i++) s.Step(inp); }
    static double FeetY(Simulation s) => s.Player.Bottom;   // scale 1: world == screen

    [Fact] public void PlayerLandsOnWindowTitleBarAndStandsStill()
    {
        var (_, s, _) = Setup();
        s.PlacePlayerAtScreen(new Vec2(400, 100));
        Run(s, 120, default);
        Assert.Near(300, FeetY(s), 0.01); Assert.True(s.Player.OnGround);
    }

    [Fact] public void WalksAcrossAndFallsOffTheEdgeOntoTheFloor()
    {
        var (_, s, _) = Setup();
        s.PlacePlayerAtScreen(new Vec2(400, 100)); Run(s, 60, default);
        Run(s, 400, new PlayerInput { Right = true });      // run right off the window (x=700)
        Run(s, 200, default);
        Assert.True(s.Player.CenterX > 700, "walked off the right side");
        Assert.Near(1040, FeetY(s), 0.01);                  // standing on the taskbar
    }

    [Fact] public void JumpRisesAndReturnsToGround()
    {
        var (_, s, _) = Setup();
        s.PlacePlayerAtScreen(new Vec2(400, 100)); Run(s, 60, default);
        double top = FeetY(s);
        double min = top;
        for (int i = 0; i < 80; i++) { s.Step(new PlayerInput { Jump = i < 20 }); min = Math.Min(min, FeetY(s)); }
        Assert.True(top - min > 60, $"jump height {top - min}");
        Assert.True(s.Player.OnGround); Assert.Near(top, FeetY(s), 0.01);
    }

    [Fact] public void AutoStepsUpOntoAdjacentWindowUpToOneTile()
    {
        // second window's top is 10 px higher than the first
        var (_, s, _) = Setup(WorldHarness.W(2, 700, 290, 400, 400, 1));
        s.PlacePlayerAtScreen(new Vec2(650, 100)); Run(s, 60, default);
        Run(s, 120, new PlayerInput { Right = true });
        Assert.True(s.Player.CenterX > 720, "stepped onto neighbour");
        Assert.Near(290, FeetY(s), 0.01);
    }

    [Fact] public void MiningTitleBarCreatesGapPlayerDropsInAndClimbsWithWS()
    {
        var (m, s, _) = Setup();
        s.PlacePlayerAtScreen(new Vec2(400, 100)); Run(s, 60, default);

        // mine two adjacent title-bar segments under the feet (player is 20 wide, segments 16)
        foreach (var aimX in new[] { 396, 412 })
        {
            var inp = new PlayerInput { Mine = true, AimScreen = new Vec2(aimX, 310) };
            for (int i = 0; i < 40 && m.Classify(aimX, 310) == Surface.Solid; i++) s.Step(inp);
            Assert.Equal(Surface.Open, m.Classify(aimX, 310));
        }
        Run(s, 120, default);                                // falls into the window
        Assert.True(s.InInterior, "player is inside the window interior");
        Assert.Near(694, FeetY(s), 0.01);                    // standing on the bottom border

        double before = s.Player.Y;
        Run(s, 20, new PlayerInput { Up = true });
        Assert.True(s.Player.Y < before - 30, "W climbs");
        double high = s.Player.Y;
        Run(s, 10, new PlayerInput { Down = true });
        Assert.True(s.Player.Y > high + 15, "S descends");
        Run(s, 5, default);
        Assert.True(s.Player.Y > high + 20, "gravity returns without W/S (standard movement unchanged)");
    }

    [Fact] public void ClimbOutThroughTheGapWithUpAndJump()
    {
        var (m, s, _) = Setup();
        s.PlacePlayerAtScreen(new Vec2(400, 100)); Run(s, 60, default);
        foreach (var aimX in new[] { 396, 412 })
            for (int i = 0; i < 40 && m.Classify(aimX, 310) == Surface.Solid; i++) s.Step(new PlayerInput { Mine = true, AimScreen = new Vec2(aimX, 310) });
        Run(s, 120, default);
        Run(s, 150, new PlayerInput { Up = true });                         // climb to the gap (362 px at 2.5 px/tick)
        Run(s, 40, new PlayerInput { Up = true, Jump = true, Right = true }); // hop out of the gap, drift right
        Run(s, 120, default);
        Assert.True(FeetY(s) <= 300.01 && s.Player.CenterY < 300, $"player ended on top, feet={FeetY(s)}");
    }

    [Fact] public void MiningRequiresReachAndHold()
    {
        var (m, s, _) = Setup();
        s.PlacePlayerAtScreen(new Vec2(400, 100)); Run(s, 60, default);
        Run(s, 100, new PlayerInput { Mine = true, AimScreen = new Vec2(396, 600) });   // far away
        Assert.Equal(0, m.MinedCount(1));
        Run(s, 5, new PlayerInput { Mine = true, AimScreen = new Vec2(396, 310) });      // not held long enough
        Assert.Equal(0, m.MinedCount(1));
        Run(s, 30, new PlayerInput { Mine = true, AimScreen = new Vec2(396, 310) });
        Assert.Equal(1, m.MinedCount(1));
    }

    [Fact] public void TaskbarCannotBeMined()
    {
        var (m, s, _) = Setup();
        s.PlacePlayerAtScreen(new Vec2(1500, 100)); Run(s, 200, default);
        Run(s, 100, new PlayerInput { Mine = true, AimScreen = new Vec2(1500, 1045) });
        Assert.Equal(Surface.Solid, m.Classify(1500, 1045));
    }

    [Fact] public void StandingOnWindowCarriesPlayerWhenWindowMoves()
    {
        var (m, s, rig) = Setup();
        s.PlacePlayerAtScreen(new Vec2(400, 100)); Run(s, 60, default);
        double x0 = s.Player.CenterX;
        for (int i = 1; i <= 10; i++)
        {
            rig.Feed(WorldHarness.Snap(i * 16, WorldHarness.Taskbar, WorldHarness.W(1, 100 + i * 10, 300 - i * 5, 600, 400, 0)));
            s.Step(default);
        }
        Assert.Near(x0 + 100, s.Player.CenterX, 2);
        Assert.Near(250, FeetY(s), 2);
    }

    [Fact] public void WindowMovedOntoPlayerLiftsThemToTheSurface()
    {
        var (m, s, rig) = Setup();
        s.PlacePlayerAtScreen(new Vec2(1300, 1040)); Run(s, 30, default);   // on the taskbar, nothing nearby
        rig.Feed(WorldHarness.Snap(100, WorldHarness.Taskbar, WorldHarness.W(1, 1000, 1000, 600, 400, 0)));
        s.Step(default);
        Assert.True(FeetY(s) <= 1000.01, $"lifted onto new window top, feet={FeetY(s)}");
    }

    [Fact] public void SlimeHopsTowardPlayerHurtsThemAndCanBeKilled()
    {
        var (m, s, _) = Setup();
        s.PlacePlayerAtScreen(new Vec2(1300, 1040)); Run(s, 30, default);
        s.SpawnSlimeAtScreen(new Vec2(1500, 1040));
        double d0 = Math.Abs(s.Enemies[0].Body.CenterX - s.Player.CenterX);
        Run(s, 90, default);
        Assert.True(s.Enemies.Count == 0 || Math.Abs(s.Enemies[0].Body.CenterX - s.Player.CenterX) < d0, "slime approaches");

        // fight: face the slime and swing repeatedly
        var killed = false;
        for (int i = 0; i < 600 && !killed; i++)
        {
            var e = s.Enemies.FirstOrDefault();
            if (e == null) break;
            bool right = e.Body.CenterX > s.Player.CenterX;
            s.Step(new PlayerInput { Attack = true, Right = right && Math.Abs(e.Body.CenterX - s.Player.CenterX) > 30, Left = !right && Math.Abs(e.Body.CenterX - s.Player.CenterX) > 30 });
            killed |= s.Events.Any(ev => ev is EnemyKilledEvent);
        }
        Assert.True(killed || s.Enemies.Count == 0, "slime killed with sword");
    }

    [Fact] public void RandomSpawnLandsOnSolidGroundAndPlayerDeathRespawns()
    {
        var (m, s, _) = Setup();
        s.PlacePlayerAtScreen(new Vec2(400, 100));
        var e = s.SpawnSlimeRandom();
        Assert.True(e != null);
        Run(s, 120, default);
        Assert.True(e!.Body.OnGround, "spawned slime rests on terrain");

        s.PlacePlayerAtScreen(new Vec2(1300, 1040)); Run(s, 10, default);
        bool died = false;
        for (int i = 0; i < 4000 && !died; i++)
        {
            if (s.Enemies.Count < 3) s.SpawnSlimeAtScreen(new Vec2(s.Player.CenterX + 30, 1040));
            s.Step(default); died |= s.Events.Any(ev => ev is PlayerDiedEvent);
        }
        Assert.True(died, "player dies to repeated slime contact");
        s.Enemies.Clear();
        Run(s, 130, default);
        Assert.True(!s.PlayerDead && s.PlayerHp == s.PlayerMaxHp);
    }

    [Fact] public void DpiScaledWorldKeepsPlayerSizeInWorldUnits()
    {
        var rig = new WorldHarness.Rig();
        var snap = new DesktopSnapshot(new[] { new MonitorInfo(0, RectI.FromXYWH(0, 0, 2880, 1620), RectI.FromXYWH(0, 0, 2880, 1560), 144, true) },
            new[] { new WindowInfo(1, 1, RectI.FromXYWH(300, 450, 900, 600), WindowState.Normal, 0, 0, 144) }, default, 0);
        rig.Feed(snap);
        var s = new Simulation(rig.Model, WorldTransform.ForDesktop(rig.Model.VirtualScreen, 144));
        s.PlacePlayerAtScreen(new Vec2(600, 100)); Run(s, 150, default);
        var feet = s.Transform.WorldToScreen(new Vec2(s.Player.CenterX, s.Player.Bottom));
        Assert.Near(450, feet.Y, 0.5);                      // title bar top in physical px
        Assert.Near(42, s.Player.H);                        // world units unchanged
    }
}
