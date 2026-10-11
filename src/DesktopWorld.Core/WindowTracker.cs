namespace DesktopWorld.Core;

public enum WindowChangeKind { Added, Removed, Moved, Resized, StateChanged, ZOrderChanged }

public sealed record WindowChange(WindowChangeKind Kind, long Id, WindowInfo? Previous, WindowInfo? Current);

/// <summary>Diffs consecutive snapshots into per-window events. Ids come from the source (HWND), so they are
/// stable for a window's lifetime. NOTE: a minimized window vanishes from snapshots (Removed) and returns as
/// Added with the same Id; the world layer should keep a grace-period tombstone to preserve mined terrain.</summary>
public sealed class WindowTracker
{
    private Dictionary<long, WindowInfo> _current = new();

    public IReadOnlyCollection<WindowInfo> Windows => _current.Values;

    public IReadOnlyList<WindowChange> Update(DesktopSnapshot snap)
    {
        var changes = new List<WindowChange>();
        var next = new Dictionary<long, WindowInfo>();
        foreach (var w in snap.Windows) next[w.Id] = w;

        foreach (var prev in _current.Values.OrderBy(p => p.ZOrder))
            if (!next.ContainsKey(prev.Id))
                changes.Add(new(WindowChangeKind.Removed, prev.Id, prev, null));

        foreach (var cur in snap.Windows)
        {
            if (!_current.TryGetValue(cur.Id, out var prev))
            {
                changes.Add(new(WindowChangeKind.Added, cur.Id, null, cur));
                continue;
            }
            if (prev.State != cur.State)
                changes.Add(new(WindowChangeKind.StateChanged, cur.Id, prev, cur));
            if (prev.Bounds != cur.Bounds)
            {
                bool size = prev.Bounds.Width != cur.Bounds.Width || prev.Bounds.Height != cur.Bounds.Height;
                changes.Add(new(size ? WindowChangeKind.Resized : WindowChangeKind.Moved, cur.Id, prev, cur));
            }
        }

        // Z-order: compare only windows that survived, so adds/removals alone don't raise events.
        var prevOrder = _current.Values.Where(v => next.ContainsKey(v.Id)).OrderBy(v => v.ZOrder).Select(v => v.Id).ToList();
        var curOrder = snap.Windows.Where(v => _current.ContainsKey(v.Id)).Select(v => v.Id).ToList();
        for (int i = 0; i < curOrder.Count && i < prevOrder.Count; i++)
            if (prevOrder[i] != curOrder[i])
                changes.Add(new(WindowChangeKind.ZOrderChanged, curOrder[i], _current[curOrder[i]], next[curOrder[i]]));

        _current = next;
        return changes;
    }
}
