using DesktopWorld.Core.World;

namespace DesktopWorld.Core.Sim;

/// <summary>
/// Headless 60 Hz platformer on top of <see cref="WorldModel"/>, in Terraria world units (16 per tile, +Y down).
/// This is a REIMPLEMENTATION of the Terraria-like rules used to validate geometry, W/S traversal, mining and the
/// bridge without the game. In the final architecture the real Terraria engine replaces it.
///
/// Movement rules (documented):
///  * Normal: A/D run, Space jumps (variable height), gravity always applies.
///  * Interiors: while the player's centre is in a window interior, holding W/S climbs (gravity suspended, like a
///    rope); without W/S normal gravity applies, so standard movement is unchanged. Space still jumps.
///  * Entering an interior requires a gap (mine the title bar or a border: the player is 20 wide, so mine two segments).
/// </summary>
public sealed class Simulation
{
    public const double Gravity = 0.4, MaxFall = 10, RunAccel = 0.2, RunFriction = 0.4, MaxRun = 3.0,
                        JumpSpeed = 5.01, ClimbSpeed = 2.5, StepUp = 16, Reach = 80;
    public const int JumpHoldTicks = 15, SwordCooldown = 25, SwordDamage = 8, PlayerInvuln = 40;
    private const double Eps = 1e-6;

    private readonly WorldModel _model;
    private WorldTransform _tf;
    private readonly Random _rng;
    private List<RectD> _solids = new();
    private int _solidsVersion = -1;

    private int _jumpTicks, _climbCoyote, _swordCd, _invuln, _respawnTimer;
    private bool _jumpPrev;
    private (long owner, PartKind part, int seg)? _mineTarget;
    private int _mineProgress;
    private long _standingOwner; private RectI _standingBounds;

    private readonly List<SimEvent> _events = new();

    public Simulation(WorldModel model, WorldTransform transform, int seed = 1)
    {
        _model = model; _tf = transform; _rng = new Random(seed);
        Player = new Body { W = 20, H = 42 };
        PlayerHp = PlayerMaxHp = 100;
    }

    public long Tick { get; private set; }
    public Body Player { get; }
    public int PlayerHp { get; private set; }
    public int PlayerMaxHp { get; }
    public bool PlayerDead => _respawnTimer > 0;
    public bool InInterior { get; private set; }
    public bool Climbing { get; private set; }
    public int MineProgress => _mineProgress;
    public List<Enemy> Enemies { get; } = new();
    public IReadOnlyList<SimEvent> Events => _events;
    public Vec2 SpawnScreen { get; set; }
    public WorldTransform Transform => _tf;

    public void SetTransform(WorldTransform t) { _tf = t; _solidsVersion = -1; }

    public void PlacePlayerAtScreen(Vec2 screen)
    {
        var w = _tf.ScreenToWorld(screen);
        Player.X = w.X - Player.W / 2; Player.Y = w.Y - Player.H;
        Player.VX = Player.VY = 0; SpawnScreen = screen;
    }

    public Enemy SpawnSlimeAtScreen(Vec2 screen)
    {
        var w = _tf.ScreenToWorld(screen);
        var e = new Enemy { Body = new Body { W = 32, H = 24, X = w.X - 16, Y = w.Y - 24 }, Hp = 14, MaxHp = 14, JumpTimer = 30 };
        Enemies.Add(e);
        _events.Add(new EnemySpawnedEvent(Tick, e.Kind, screen));
        return e;
    }

    /// <summary>Spawns a slime above a random solid surface (not inside interiors). Returns null if there is none.</summary>
    public Enemy? SpawnSlimeRandom()
    {
        RefreshSolids();
        var candidates = _solids.Where(r => r.Width >= 40).ToList();
        if (candidates.Count == 0) return null;
        var r = candidates[_rng.Next(candidates.Count)];
        double x = r.Left + 20 + _rng.NextDouble() * (r.Width - 40);
        return SpawnSlimeAtScreen(_tf.WorldToScreen(new Vec2(x, r.Top - 1)));
    }

    // ---------------------------------------------------------------- step
    public void Step(PlayerInput input)
    {
        _events.Clear();
        Tick++;
        if (!PlayerDead) CarryWithPlatform(Player);   // before depenetration, so a moving window never lifts AND carries
        RefreshSolids();

        if (PlayerDead)
        {
            if (--_respawnTimer == 0) Respawn();
        }
        else StepPlayer(input);

        foreach (var e in Enemies.ToList()) StepEnemy(e);
        ResolveContacts();
        CatchFallen();
    }

    private void RefreshSolids()
    {
        if (_solidsVersion == _model.Version) return;
        _solids = _model.CollisionRects.Select(_tf.ToWorld).ToList();
        _solidsVersion = _model.Version;
        Depenetrate(Player);
        foreach (var e in Enemies) Depenetrate(e.Body);
    }

    private void StepPlayer(PlayerInput inp)
    {
        var p = Player;

        var c = _tf.WorldToScreen(new Vec2(p.CenterX, p.CenterY));
        InInterior = _model.IsInterior((int)Math.Floor(c.X), (int)Math.Floor(c.Y));

        double dir = (inp.Right ? 1 : 0) - (inp.Left ? 1 : 0);
        if (dir != 0)
        {
            p.VX += dir * RunAccel * (Math.Sign(p.VX) == -dir ? 3 : 1);
            p.VX = Math.Clamp(p.VX, -MaxRun, MaxRun);
            p.Facing = (int)dir;
        }
        else p.VX = Math.Abs(p.VX) <= RunFriction ? 0 : p.VX - Math.Sign(p.VX) * RunFriction;

        bool vert = inp.Up ^ inp.Down;
        Climbing = InInterior && vert && _jumpTicks == 0;
        if (Climbing) _climbCoyote = 6; else if (_climbCoyote > 0) _climbCoyote--;

        bool jumpPressed = inp.Jump && !_jumpPrev;
        if (jumpPressed && (p.OnGround || _climbCoyote > 0)) { p.VY = -JumpSpeed; _jumpTicks = JumpHoldTicks; p.OnGround = false; _climbCoyote = 0; Climbing = false; }
        else if (inp.Jump && _jumpTicks > 0) { p.VY = -JumpSpeed; _jumpTicks--; }
        else _jumpTicks = 0;
        _jumpPrev = inp.Jump;

        if (Climbing) p.VY = inp.Up ? -ClimbSpeed : ClimbSpeed;
        else if (_jumpTicks == 0 || !inp.Jump) p.VY = Math.Min(p.VY + Gravity, MaxFall);

        MoveX(p, p.VX, canStep: true);
        bool hitHead = MoveY(p, p.VY);
        if (hitHead) _jumpTicks = 0;

        StepMining(inp);
        if (_swordCd > 0) _swordCd--;
        if (inp.Attack && _swordCd == 0) Attack();
        if (_invuln > 0) _invuln--;
        RememberPlatform(p);
    }

    // ---------------------------------------------------------------- mining
    private void StepMining(PlayerInput inp)
    {
        if (!inp.Mine) { _mineTarget = null; _mineProgress = 0; return; }
        var aimW = _tf.ScreenToWorld(inp.AimScreen);
        double dx = aimW.X - Player.CenterX, dy = aimW.Y - Player.CenterY;
        if (dx * dx + dy * dy > Reach * Reach) { _mineTarget = null; _mineProgress = 0; return; }
        int sx = (int)Math.Floor(inp.AimScreen.X), sy = (int)Math.Floor(inp.AimScreen.Y);
        var piece = _model.Pieces.FirstOrDefault(pc => pc.Rect.Contains(sx, sy));
        if (piece is null || piece.OwnerId <= 0) { _mineTarget = null; _mineProgress = 0; return; }

        var key = (piece.OwnerId, piece.Part, piece.Segment);
        if (_mineTarget != key) { _mineTarget = key; _mineProgress = 0; }
        if (++_mineProgress >= WorldModel.MineTicks(piece.Part))
        {
            var r = _model.TryMine(sx, sy);
            _events.Add(new MinedEvent(Tick, r));
            _mineTarget = null; _mineProgress = 0;
            RefreshSolids();
        }
    }

    // ---------------------------------------------------------------- combat
    private void Attack()
    {
        var p = Player; _swordCd = SwordCooldown;
        double left = p.Facing > 0 ? p.X + p.W : p.X - 40;
        var hit = new RectD(left, p.Y, left + 40, p.Y + p.H);
        foreach (var e in Enemies.ToList())
        {
            var b = e.Body;
            if (b.X >= hit.Right || b.X + b.W <= hit.Left || b.Y >= hit.Bottom || b.Bottom <= hit.Top || e.Invuln > 0) continue;
            e.Hp -= SwordDamage; e.Invuln = 12;
            b.VX = p.Facing * 4; b.VY = -3; b.OnGround = false;
            _events.Add(new EnemyHitEvent(Tick, e.Kind, SwordDamage, Math.Max(e.Hp, 0)));
            if (e.Hp <= 0) { Enemies.Remove(e); _events.Add(new EnemyKilledEvent(Tick, e.Kind)); }
        }
    }

    private void StepEnemy(Enemy e)
    {
        var b = e.Body;
        if (e.Invuln > 0) e.Invuln--;
        if (b.OnGround)
        {
            b.VX *= 0.8; if (Math.Abs(b.VX) < 0.05) b.VX = 0;
            if (--e.JumpTimer <= 0 && !PlayerDead)
            {
                double dir = Math.Sign(Player.CenterX - b.CenterX);
                if (dir == 0) dir = 1;
                b.VX = dir * 2.5; b.VY = -5.5; b.OnGround = false; b.Facing = (int)dir;
                e.JumpTimer = 50 + _rng.Next(50);
            }
        }
        b.VY = Math.Min(b.VY + Gravity, MaxFall);
        MoveX(b, b.VX, canStep: false);
        MoveY(b, b.VY);
    }

    private void ResolveContacts()
    {
        if (PlayerDead || _invuln > 0) return;
        var p = Player;
        foreach (var e in Enemies)
        {
            var b = e.Body;
            if (b.X >= p.X + p.W || b.X + b.W <= p.X || b.Y >= p.Bottom || b.Bottom <= p.Y) continue;
            PlayerHp -= 7; _invuln = PlayerInvuln;
            p.VX = Math.Sign(p.CenterX - b.CenterX) * 4; p.VY = -3; p.OnGround = false;
            _events.Add(new PlayerHurtEvent(Tick, 7, Math.Max(PlayerHp, 0)));
            if (PlayerHp <= 0) { _respawnTimer = 120; _events.Add(new PlayerDiedEvent(Tick)); }
            return;
        }
    }

    private void Respawn()
    {
        PlayerHp = PlayerMaxHp; _invuln = 120; PlacePlayerAtScreen(SpawnScreen);
        _events.Add(new PlayerRespawnedEvent(Tick));
    }

    private void CatchFallen()
    {
        var vs = _model.VirtualScreen;
        if (vs.IsEmpty) return;
        var bottomW = _tf.ScreenToWorld(new Vec2(0, vs.Bottom + 200)).Y;
        var leftW = _tf.ScreenToWorld(new Vec2(vs.Left, 0)).X; var rightW = _tf.ScreenToWorld(new Vec2(vs.Right, 0)).X;
        if (Player.Y > bottomW && !PlayerDead) PlacePlayerAtScreen(SpawnScreen);
        Player.X = Math.Clamp(Player.X, leftW, rightW - Player.W);
        Enemies.RemoveAll(e => e.Body.Y > bottomW);
    }

    // ---------------------------------------------------------------- platform carry
    private void RememberPlatform(Body p)
    {
        _standingOwner = 0;
        if (!p.OnGround) return;
        var s = _tf.WorldToScreen(new Vec2(p.CenterX, p.Bottom + 0.5));
        if (_model.Classify((int)Math.Floor(s.X), (int)Math.Floor(s.Y), out var owner) == Surface.Solid && owner > 0 && _model.BoundsOf(owner) is RectI b)
        { _standingOwner = owner; _standingBounds = b; }
    }

    private void CarryWithPlatform(Body p)
    {
        if (_standingOwner == 0) return;
        if (_model.BoundsOf(_standingOwner) is not RectI now) { _standingOwner = 0; return; }
        int dx = now.Left - _standingBounds.Left, dy = now.Top - _standingBounds.Top;
        if (dx != 0 || dy != 0) { p.X += dx / _tf.PixelsPerWorldUnit; p.Y += dy / _tf.PixelsPerWorldUnit; }
        _standingBounds = now;
    }

    // ---------------------------------------------------------------- collision
    private static bool Overlaps(double x, double y, double w, double h, RectD r) =>
        x < r.Right - Eps && x + w > r.Left + Eps && y < r.Bottom - Eps && y + h > r.Top + Eps;

    private bool Free(double x, double y, double w, double h) => !_solids.Any(r => Overlaps(x, y, w, h, r));

    private void MoveX(Body b, double dx, bool canStep)
    {
        if (dx == 0) return;
        double x0 = b.X, target = x0 + dx;
        bool blocked = false; double minTop = double.MaxValue;
        foreach (var r in _solids)
        {
            if (!(b.Y < r.Bottom - Eps && b.Bottom > r.Top + Eps)) continue;
            if (dx > 0 && r.Left >= x0 + b.W - Eps && r.Left < target + b.W) { target = r.Left - b.W; blocked = true; minTop = Math.Min(minTop, r.Top); }
            else if (dx < 0 && r.Right <= x0 + Eps && r.Right > target) { target = r.Right; blocked = true; minTop = Math.Min(minTop, r.Top); }
        }
        if (blocked && canStep && b.OnGround)
        {
            double rise = b.Bottom - minTop;
            if (rise > 0 && rise <= StepUp && Free(x0 + dx, b.Y - rise - Eps, b.W, b.H)) { b.Y -= rise + Eps; b.X = x0 + dx; return; }
        }
        b.X = target;
        if (blocked) b.VX = 0;
    }

    /// <returns>true if the body hit a ceiling.</returns>
    private bool MoveY(Body b, double dy)
    {
        double y0 = b.Y, target = y0 + dy; bool hitHead = false;
        bool landed = false;
        foreach (var r in _solids)
        {
            if (!(b.X < r.Right - Eps && b.X + b.W > r.Left + Eps)) continue;
            if (dy > 0 && r.Top >= y0 + b.H - Eps && r.Top < target + b.H) { target = r.Top - b.H; landed = true; }
            else if (dy < 0 && r.Bottom <= y0 + Eps && r.Bottom > target) { target = r.Bottom; hitHead = true; }
        }
        b.Y = target;
        if (landed || hitHead) b.VY = 0;
        b.OnGround = landed;
        return hitHead;
    }

    /// <summary>If a moved window left a body embedded in terrain, lift it to the surface (windows can move onto the player).</summary>
    private void Depenetrate(Body b)
    {
        for (int i = 0; i < 8; i++)
        {
            var r = _solids.FirstOrDefault(s => Overlaps(b.X, b.Y, b.W, b.H, s));
            if (r.Equals(default(RectD))) return;
            b.Y = r.Top - b.H - Eps; b.VY = 0;
        }
    }
}
