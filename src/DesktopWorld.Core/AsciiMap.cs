using System.Text;

namespace DesktopWorld.Core;

/// <summary>Text rendering of the desktop-world classification for debugging.
/// '=' title bar, '#' border (solid), '.' interior (traversable), 'T' taskbar, ' ' open desktop.
/// Overlaps resolve by z-order (topmost window wins), mirroring what is visible on screen.</summary>
public static class AsciiMap
{
    public static string Render(DesktopSnapshot snap, int cols, int rows)
    {
        var vs = snap.VirtualScreen;
        if (vs.IsEmpty || cols < 1 || rows < 1) return string.Empty;

        var parts = snap.Windows.OrderBy(w => w.ZOrder).Select(w => (w, p: WindowGeometry.Decompose(w))).ToList();
        var sb = new StringBuilder(rows * (cols + 1));
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int x = vs.Left + (int)((c + 0.5) * vs.Width / cols);
                int y = vs.Top + (int)((r + 0.5) * vs.Height / rows);
                sb.Append(Classify(snap, parts, x, y));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static char Classify(DesktopSnapshot snap, List<(WindowInfo w, WindowParts p)> parts, int x, int y)
    {
        foreach (var (_, p) in parts)
        {
            if (!p.Outer.Contains(x, y)) continue;
            if (p.TitleBar.Contains(x, y)) return '=';
            if (p.Interior.Contains(x, y)) return '.';
            return '#';
        }
        return snap.Taskbar.Contains(x, y) ? 'T' : ' ';
    }
}
