using DesktopWorld.Core;
using DesktopWorld.Core.World;
using DesktopWorld.Protocol;

namespace DesktopWorld.Bridge;

/// <summary>Glue between <see cref="WorldModel"/> and a connected mod: pushes full geometry on connect and when the world
/// version changes (throttled), applies the mod's MINE events to the simulated terrain, and relays gameplay mode.
/// Geometry travels as full state with a version; deltas are a future optimisation (see docs).</summary>
public sealed class CompanionBridge
{
    private readonly PipeServer _server;
    private readonly WorldModel _model;
    private readonly Func<WorldTransform> _transform;
    private readonly TimeSpan _minInterval;
    private int _sentVersion = -1;
    private long _lastSendTicks;

    public bool ModeActive { get; private set; }
    public bool ModePaused { get; private set; }
    public PlayerStateMsg? LastPlayerState { get; private set; }
    public event Action<string>? Log;

    public CompanionBridge(PipeServer server, WorldModel model, Func<WorldTransform> transform, TimeSpan? minInterval = null)
    {
        _server = server; _model = model; _transform = transform; _minInterval = minInterval ?? TimeSpan.FromMilliseconds(50);
        server.ConnectionChanged += up => { if (up) { _sentVersion = -1; PushGeometry(force: true); SendMode(); } else LastPlayerState = null; };
        server.Received += OnMessage;
    }

    public GeometryMsg BuildGeometry()
    {
        var t = _transform();
        var g = new GeometryMsg { WorldVersion = _model.Version, PixelsPerWorldUnit = t.PixelsPerWorldUnit, OriginX = (int)Math.Round(t.ScreenOrigin.X), OriginY = (int)Math.Round(t.ScreenOrigin.Y) };
        foreach (var r in _model.CollisionRects.Take(Wire.MaxListItems)) g.Solids.Add(new Rect(r.Left, r.Top, r.Right, r.Bottom));
        foreach (var i in _model.Interiors.Take(Wire.MaxListItems)) g.Interiors.Add(new Rect(i.Rect.Left, i.Rect.Top, i.Rect.Right, i.Rect.Bottom));
        return g;
    }

    /// <summary>Call regularly (e.g. every frame). Sends only if the world changed and the throttle interval has passed.</summary>
    public bool PushGeometry(bool force = false)
    {
        if (!_server.Connected) return false;
        if (!force && _model.Version == _sentVersion) return false;
        long now = Environment.TickCount64;
        if (!force && now - _lastSendTicks < _minInterval.TotalMilliseconds) return false;
        if (!_server.Send(BuildGeometry())) return false;
        _sentVersion = _model.Version; _lastSendTicks = now;
        return true;
    }

    public void SetMode(bool active, bool paused) { ModeActive = active; ModePaused = paused; SendMode(); }
    public void SendSpawn(string kind, int x = -1, int y = -1) => _server.Send(new SpawnMsg { Kind = kind, X = x, Y = y });
    private void SendMode() => _server.Send(new ModeMsg { Active = ModeActive, Paused = ModePaused });

    private void OnMessage(Msg m)
    {
        switch (m)
        {
            case MineMsg mine:
                var r = _model.TryMine(mine.X, mine.Y);
                Log?.Invoke($"mod mine ({mine.X},{mine.Y}) -> {r.Outcome}");
                PushGeometry(force: true);
                break;
            case PlayerStateMsg ps: LastPlayerState = ps; break;
            case ModeMsg mm: ModeActive = mm.Active; ModePaused = mm.Paused; break;   // mod may request pause; companion stays authoritative on input
            case ErrorMsg e: Log?.Invoke($"peer error {e.Code}: {e.Text}"); break;
        }
    }
}
