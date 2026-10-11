namespace DesktopWorld.Core.World;

public static class RectOps
{
    /// <summary>Appends a minus b (up to 4 disjoint rects) to <paramref name="result"/>.</summary>
    public static void Subtract(RectI a, RectI b, List<RectI> result)
    {
        if (a.IsEmpty) return;
        var i = a.Intersect(b);
        if (i.IsEmpty) { result.Add(a); return; }
        if (i.Top > a.Top) result.Add(new(a.Left, a.Top, a.Right, i.Top));
        if (i.Bottom < a.Bottom) result.Add(new(a.Left, i.Bottom, a.Right, a.Bottom));
        if (i.Left > a.Left) result.Add(new(a.Left, i.Top, i.Left, i.Bottom));
        if (i.Right < a.Right) result.Add(new(i.Right, i.Top, a.Right, i.Bottom));
    }

    public static List<RectI> SubtractAll(RectI a, IReadOnlyList<RectI> covers)
    {
        var cur = new List<RectI> { a };
        var next = new List<RectI>();
        foreach (var c in covers)
        {
            if (cur.Count == 0) break;
            next.Clear();
            foreach (var r in cur) Subtract(r, c, next);
            (cur, next) = (next, cur);
        }
        return cur;
    }
}
