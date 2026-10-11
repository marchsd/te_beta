namespace DesktopWorld.Core.World;

public enum PartKind { TitleBar, Left, Right, Bottom, Taskbar, Floor }
public enum Surface { Open, Solid, Interior }
public enum MineOutcome { Nothing, Mined, Unmineable }

public readonly record struct CellKey(PartKind Part, int Segment);

/// <summary>A visible, un-mined solid fragment (screen px). OwnerId 0 = taskbar, -1 = floor.</summary>
public sealed record SolidPiece(long OwnerId, PartKind Part, int Segment, RectI Rect);
/// <summary>A visible traversable fragment of a window interior.</summary>
public sealed record InteriorPiece(long OwnerId, RectI Rect);
public sealed record MineResult(MineOutcome Outcome, long OwnerId, PartKind Part, int Segment, RectI Rect);

public sealed class WorldModelOptions
{
    public TimeSpan TombstoneGrace { get; init; } = TimeSpan.FromSeconds(30);
    public bool FloorWhenNoTaskbar { get; init; } = true;
    public int FloorThicknessPx { get; init; } = 8;
    /// <summary>Terraria tile edge in world units; mining granularity along a strip.</summary>
    public double TileWorldUnits { get; init; } = WorldTransform.TileSize;
}

/// <summary>
/// Turns desktop snapshots into gameplay geometry and owns all terrain edits.
///
/// Rules (documented behaviour):
///  * Solid: title bar + left/right/bottom borders, split into tile-length SEGMENTS along the strip.
///    Interior is traversable. Maximized: no side/bottom borders. Fullscreen windows are occluders only.
///  * Occlusion: any part covered by a higher-z window (or the taskbar) is not solid/traversable.
///  * Mining removes one segment (Part, index). Indices count from the strip start (title bar and bottom: from
///    the left; left/right: from the top), so holes follow window moves, stay on the same border after resize,
///    and are remembered (but inactive) if a shrink pushes them out of range.
///  * Closing/minimizing keeps edits in a tombstone for TombstoneGrace; the same window id returning within
///    that time gets its holes back. Edits never touch the real window.
///  * Only windows whose bounds/state/dpi changed have parts recomputed; piece lists are rebuilt only when
///    something changed (see <see cref="RebuildCount"/>/<see cref="PartsRecomputed"/>).
/// </summary>
public sealed class WorldModel
{
    private sealed class Entity
    {
        public WindowInfo Info = null!;
        public WindowParts Parts = null!;
        public readonly HashSet<CellKey> Mined = new();
        public long? TombstonedAtMs;
    }

    private readonly WorldModelOptions _opt;
    private readonly Dictionary<long, Entity> _entities = new();
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private RectI _taskbar;
    private long _nowMs;

    private List<SolidPiece> _pieces = new();
    private List<RectI> _collision = new();
    private List<InteriorPiece> _interiors = new();

    public WorldModel(WorldModelOptions? options = null) => _opt = options ?? new();

    public int Version { get; private set; }
    public int RebuildCount { get; private set; }
    public int PartsRecomputed { get; private set; }
    public IReadOnlyList<SolidPiece> Pieces => _pieces;
    /// <summary>Merged solid rectangles for collision (no seams between segments).</summary>
    public IReadOnlyList<RectI> CollisionRects => _collision;
    public IReadOnlyList<InteriorPiece> Interiors => _interiors;
    public RectI VirtualScreen => _monitors.Aggregate(default(RectI), (a, m) => a.Union(m.Bounds));
    public int TombstoneCount => _entities.Values.Count(e => e.TombstonedAtMs.HasValue);
    public RectI? BoundsOf(long windowId) => _entities.TryGetValue(windowId, out var e) && !e.TombstonedAtMs.HasValue ? e.Info.Bounds : null;
    public int MinedCount(long windowId) => _entities.TryGetValue(windowId, out var e) ? e.Mined.Count : 0;

    public bool Apply(DesktopSnapshot snap, IReadOnlyList<WindowChange> changes)
    {
        _nowMs = snap.TimestampMs;
        bool dirty = Version == 0;

        if (!_monitors.SequenceEqual(snap.Monitors)) { _monitors = snap.Monitors; dirty = true; }
        if (_taskbar != snap.Taskbar) { _taskbar = snap.Taskbar; dirty = true; }

        foreach (var c in changes)
        {
            if (c.Kind == WindowChangeKind.Removed)
            {
                if (_entities.TryGetValue(c.Id, out var gone)) gone.TombstonedAtMs = snap.TimestampMs;
                dirty = true;
            }
            else dirty = true;
        }

        var seen = new HashSet<long>();
        foreach (var w in snap.Windows)
        {
            seen.Add(w.Id);
            if (!_entities.TryGetValue(w.Id, out var e)) _entities[w.Id] = e = new Entity();
            e.TombstonedAtMs = null;
            bool geometryChanged = e.Info is null || e.Info.Bounds != w.Bounds || e.Info.State != w.State || e.Info.Dpi != w.Dpi;
            if (e.Info is null || e.Info != w) dirty |= e.Info is null || e.Info.ZOrder != w.ZOrder || geometryChanged;
            e.Info = w;
            if (geometryChanged) { e.Parts = WindowGeometry.Decompose(w); PartsRecomputed++; }
        }

        // Windows absent from the snapshot become tombstones (covers sources that skip Removed events).
        foreach (var (id, e) in _entities)
            if (!seen.Contains(id) && !e.TombstonedAtMs.HasValue) { e.TombstonedAtMs = snap.TimestampMs; dirty = true; }

        long graceMs = (long)_opt.TombstoneGrace.TotalMilliseconds;
        foreach (var id in _entities.Where(kv => kv.Value.TombstonedAtMs is long t && snap.TimestampMs - t > graceMs).Select(kv => kv.Key).ToList())
            _entities.Remove(id);

        if (dirty) Rebuild();
        return dirty;
    }

    private IEnumerable<Entity> Live() => _entities.Values.Where(e => !e.TombstonedAtMs.HasValue).OrderBy(e => e.Info.ZOrder);

    private void Rebuild()
    {
        RebuildCount++;
        Version++;
        var pieces = new List<SolidPiece>();
        var interiors = new List<InteriorPiece>();
        var covers = new List<RectI>();
        if (!_taskbar.IsEmpty) covers.Add(_taskbar);

        foreach (var e in Live())
        {
            var p = e.Parts;
            if (e.Info.State != WindowState.Fullscreen)
            {
                int seg = Math.Max(4, (int)Math.Round(_opt.TileWorldUnits * e.Info.Dpi / 96.0));
                AddStrip(pieces, covers, e, PartKind.TitleBar, p.TitleBar, horizontal: true, seg);
                AddStrip(pieces, covers, e, PartKind.Left, p.Left, horizontal: false, seg);
                AddStrip(pieces, covers, e, PartKind.Right, p.Right, horizontal: false, seg);
                AddStrip(pieces, covers, e, PartKind.Bottom, p.Bottom, horizontal: true, seg);
                foreach (var r in RectOps.SubtractAll(p.Interior, covers)) interiors.Add(new(e.Info.Id, r));
            }
            covers.Add(p.Outer);
        }

        // Taskbar is only hidden by fullscreen windows; floor is the fallback ground when no taskbar.
        var fullscreen = Live().Where(e => e.Info.State == WindowState.Fullscreen).Select(e => e.Info.Bounds).ToList();
        if (!_taskbar.IsEmpty)
            foreach (var r in RectOps.SubtractAll(_taskbar, fullscreen)) pieces.Add(new(0, PartKind.Taskbar, 0, r));
        else if (_opt.FloorWhenNoTaskbar)
            foreach (var m in _monitors)
                pieces.Add(new(-1, PartKind.Floor, m.Index, new RectI(m.Bounds.Left, m.Bounds.Bottom - _opt.FloorThicknessPx, m.Bounds.Right, m.Bounds.Bottom)));

        _pieces = pieces;
        _interiors = interiors;
        _collision = Merge(pieces);
    }

    private void AddStrip(List<SolidPiece> pieces, List<RectI> covers, Entity e, PartKind kind, RectI strip, bool horizontal, int seg)
    {
        if (strip.IsEmpty) return;
        int length = horizontal ? strip.Width : strip.Height;
        int count = (length + seg - 1) / seg;
        for (int i = 0; i < count; i++)
        {
            if (e.Mined.Contains(new CellKey(kind, i))) continue;
            var cell = horizontal
                ? new RectI(strip.Left + i * seg, strip.Top, Math.Min(strip.Left + (i + 1) * seg, strip.Right), strip.Bottom)
                : new RectI(strip.Left, strip.Top + i * seg, strip.Right, Math.Min(strip.Top + (i + 1) * seg, strip.Bottom));
            foreach (var r in RectOps.SubtractAll(cell, covers)) pieces.Add(new(e.Info.Id, kind, i, r));
        }
    }

    private static List<RectI> Merge(List<SolidPiece> pieces)
    {
        var result = new List<RectI>();
        foreach (var g in pieces.GroupBy(p => (p.OwnerId, p.Part)))
        {
            var rects = g.Select(p => p.Rect).OrderBy(r => r.Top).ThenBy(r => r.Left).ToList();
            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < rects.Count && !merged; i++)
                    for (int j = i + 1; j < rects.Count && !merged; j++)
                    {
                        var a = rects[i]; var b = rects[j];
                        RectI? m = null;
                        if (a.Top == b.Top && a.Bottom == b.Bottom && (a.Right == b.Left || b.Right == a.Left)) m = a.Union(b);
                        else if (a.Left == b.Left && a.Right == b.Right && (a.Bottom == b.Top || b.Bottom == a.Top)) m = a.Union(b);
                        if (m is RectI mm) { rects[i] = mm; rects.RemoveAt(j); merged = true; }
                    }
            } while (merged);
            result.AddRange(rects);
        }
        return result;
    }

    public Surface Classify(int x, int y, out long ownerId)
    {
        foreach (var p in _pieces) if (p.Rect.Contains(x, y)) { ownerId = p.OwnerId; return Surface.Solid; }
        foreach (var i in _interiors) if (i.Rect.Contains(x, y)) { ownerId = i.OwnerId; return Surface.Interior; }
        ownerId = long.MinValue;
        return Surface.Open;
    }

    public Surface Classify(int x, int y) => Classify(x, y, out _);

    public bool IsInterior(int x, int y) => _interiors.Any(i => i.Rect.Contains(x, y));

    /// <summary>Mines the segment under a screen point. Taskbar/floor are unmineable. Edits simulated terrain only.</summary>
    public MineResult TryMine(int x, int y)
    {
        var piece = _pieces.FirstOrDefault(p => p.Rect.Contains(x, y));
        if (piece is null) return new(MineOutcome.Nothing, 0, default, 0, default);
        if (piece.OwnerId <= 0) return new(MineOutcome.Unmineable, piece.OwnerId, piece.Part, piece.Segment, piece.Rect);
        _entities[piece.OwnerId].Mined.Add(new CellKey(piece.Part, piece.Segment));
        Rebuild();
        return new(MineOutcome.Mined, piece.OwnerId, piece.Part, piece.Segment, piece.Rect);
    }

    /// <summary>Segment hardness in sim ticks of mining (title bars are tougher than borders).</summary>
    public static int MineTicks(PartKind part) => part switch { PartKind.TitleBar => 24, PartKind.Bottom => 18, _ => 14 };

    public IEnumerable<RectI> CollisionNear(RectI area) => _collision.Where(r => r.IntersectsWith(area));
}
