using System.Text;
using DesktopWorld.Core.World;

namespace DesktopWorld.Core.Sim;

public static class SimAscii
{
    /// <summary>'=' title bar, '#' border, 'T' taskbar, '_' floor, '.' interior, '@' player (X if dead), 'S' slime.</summary>
    public static string Render(Simulation sim, WorldModel model, int cols, int rows)
    {
        var vs = model.VirtualScreen;
        if (vs.IsEmpty) return "";
        var sb = new StringBuilder();
        char[][] grid = new char[rows][];
        for (int r = 0; r < rows; r++)
        {
            grid[r] = new char[cols];
            for (int c = 0; c < cols; c++)
            {
                int x = vs.Left + (int)((c + 0.5) * vs.Width / cols), y = vs.Top + (int)((r + 0.5) * vs.Height / rows);
                var piece = model.Pieces.FirstOrDefault(p => p.Rect.Contains(x, y));
                grid[r][c] = piece != null
                    ? piece.Part switch { PartKind.TitleBar => '=', PartKind.Taskbar => 'T', PartKind.Floor => '_', _ => '#' }
                    : model.IsInterior(x, y) ? '.' : ' ';
            }
        }
        void Mark(Body b, char ch)
        {
            var s = sim.Transform.WorldToScreen(new Vec2(b.CenterX, b.CenterY));
            int c = (int)((s.X - vs.Left) * cols / vs.Width), r = (int)((s.Y - vs.Top) * rows / vs.Height);
            if (c >= 0 && c < cols && r >= 0 && r < rows) grid[r][c] = ch;
        }
        foreach (var e in sim.Enemies) Mark(e.Body, 'S');
        Mark(sim.Player, sim.PlayerDead ? 'X' : '@');
        foreach (var row in grid) sb.Append(row).Append('\n');
        return sb.ToString();
    }
}
