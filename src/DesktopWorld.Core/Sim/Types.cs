using DesktopWorld.Core.World;

namespace DesktopWorld.Core.Sim;

public struct PlayerInput
{
    public bool Left, Right, Jump, Up, Down, Mine, Attack;
    /// <summary>Mining aim in physical screen pixels.</summary>
    public Vec2 AimScreen;
}

public sealed class Body
{
    public double X, Y, W, H, VX, VY;     // world units, top-left origin
    public bool OnGround;
    public int Facing = 1;
    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;
    public double Bottom => Y + H;
}

public sealed class Enemy
{
    public required Body Body { get; init; }
    public int Hp;
    public int MaxHp;
    public int JumpTimer;
    public int Invuln;
    public string Kind = "Blue Slime";
}

public abstract record SimEvent(long Tick);
public sealed record MinedEvent(long Tick, MineResult Result) : SimEvent(Tick);
public sealed record EnemySpawnedEvent(long Tick, string Kind, Vec2 Screen) : SimEvent(Tick);
public sealed record EnemyHitEvent(long Tick, string Kind, int Damage, int HpLeft) : SimEvent(Tick);
public sealed record EnemyKilledEvent(long Tick, string Kind) : SimEvent(Tick);
public sealed record PlayerHurtEvent(long Tick, int Damage, int HpLeft) : SimEvent(Tick);
public sealed record PlayerDiedEvent(long Tick) : SimEvent(Tick);
public sealed record PlayerRespawnedEvent(long Tick) : SimEvent(Tick);
