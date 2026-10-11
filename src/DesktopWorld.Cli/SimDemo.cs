using DesktopWorld.Core;
using DesktopWorld.Core.Sim;
using DesktopWorld.Core.World;

namespace DesktopWorld.Cli;

/// <summary>Scripted, non-interactive walkthrough of the simulation on the captured desktop geometry (real or mock).</summary>
internal static class SimDemo
{
    public static void Run(IDesktopSource source)
    {
        var snap = source.Capture();
        var tracker = new WindowTracker();
        var model = new WorldModel();
        model.Apply(snap, tracker.Update(snap));
        var tf = WorldTransform.ForDesktop(model.VirtualScreen, snap.Primary?.Dpi ?? 96);
        var sim = new Simulation(model, tf);

        // Pick the largest window with room above it, else bail.
        var target = model.Interiors.OrderByDescending(i => i.Rect.Area).FirstOrDefault();
        if (target is null) { Console.WriteLine("No window interior found to demo on."); return; }
        var bounds = model.BoundsOf(target.OwnerId)!.Value;
        int cx = bounds.Left + bounds.Width / 2;
        sim.PlacePlayerAtScreen(new Vec2(cx, Math.Max(model.VirtualScreen.Top + 50, bounds.Top - 120)));

        void Frame(string title)
        {
            Console.WriteLine($"--- {title}  (tick {sim.Tick}, hp {sim.PlayerHp}, interior={sim.InInterior}, enemies={sim.Enemies.Count})");
            Console.Write(SimAscii.Render(sim, model, 100, 28));
        }
        void Run(int ticks, PlayerInput inp) { for (int i = 0; i < ticks; i++) sim.Step(inp); }

        Run(200, default);
        Frame("1. landed on top of a window");
        Run(60, new PlayerInput { Right = true });
        Frame("2. walked right along the title bar");

        int aimY = bounds.Top + 8;
        foreach (int dx in new[] { -8, 8 })
        {
            var aim = new Vec2((int)(tf.WorldToScreen(new Vec2(sim.Player.CenterX, 0)).X) + dx, aimY);
            var inp = new PlayerInput { Mine = true, AimScreen = aim };
            for (int i = 0; i < 60 && model.Classify((int)aim.X, (int)aim.Y) == Surface.Solid; i++) sim.Step(inp);
        }
        Frame("3. mined a gap in the title bar");
        Run(200, default);
        Frame("4. dropped into the window interior");
        Run(40, new PlayerInput { Up = true });
        Frame("5. climbing with W");
        sim.SpawnSlimeAtScreen(new Vec2(bounds.Left + 60, bounds.Bottom - 20));
        Run(120, default);
        Frame("6. a slime spawned inside the window and is hopping at the player");
    }
}
