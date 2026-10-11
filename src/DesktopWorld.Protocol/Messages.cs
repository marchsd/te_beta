#nullable disable
using System.Collections.Generic;

namespace DesktopWorld.Protocol
{
    /// <summary>Wire format (ASCII, one message per line, max 256 KB):  DW1|TYPE|seq|field|field...
    /// Lists use ';' between items and ',' inside items. No window titles or text ever travel on the wire.
    /// The "DW1" prefix is the protocol major version: a different prefix is rejected. Unknown TYPEs under DW1 decode as
    /// <see cref="UnknownMsg"/> so newer peers can add messages without breaking older ones.</summary>
    public static class Wire
    {
        public const string Version = "DW1";
        public const int MaxLineBytes = 256 * 1024;
        public const int MaxListItems = 5000;
        public const string PipeName = "DesktopWorld.v1";
    }

    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public Rect(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
        public bool IsEmpty { get { return Right <= Left || Bottom <= Top; } }
        public override string ToString() { return Left + "," + Top + "," + Right + "," + Bottom; }
    }

    public abstract class Msg { public long Seq; }

    /// <summary>First message from a connecting peer. Role is "mod" or "companion".</summary>
    public sealed class HelloMsg : Msg { public string Role; public string AppVersion; }
    public sealed class WelcomeMsg : Msg { public string AppVersion; }
    public sealed class PingMsg : Msg { public long TimestampMs; }
    public sealed class PongMsg : Msg { public long TimestampMs; }

    /// <summary>Full terrain state (companion -> mod). Sent on connect and whenever the world version changes
    /// (throttled by the sender). Rects are PHYSICAL screen pixels; world = (screen - origin) / PixelsPerWorldUnit.</summary>
    public sealed class GeometryMsg : Msg
    {
        public int WorldVersion;
        public double PixelsPerWorldUnit;
        public int OriginX, OriginY;
        public List<Rect> Solids = new List<Rect>();
        public List<Rect> Interiors = new List<Rect>();
    }

    /// <summary>Gameplay activation (either direction; the companion owns input mode and is authoritative).</summary>
    public sealed class ModeMsg : Msg { public bool Active; public bool Paused; }
    /// <summary>Mod -> companion: the player mined at this screen pixel. Companion edits the simulated terrain.</summary>
    public sealed class MineMsg : Msg { public int X, Y; }
    /// <summary>Companion -> mod: spawn an enemy. X,Y = -1,-1 means "pick a spot".</summary>
    public sealed class SpawnMsg : Msg { public string Kind; public int X = -1, Y = -1; }
    /// <summary>Mod -> companion at ~10 Hz while active: HUD data and the player's screen position.</summary>
    public sealed class PlayerStateMsg : Msg { public int Hp, MaxHp, X, Y; public bool InInterior; }
    public sealed class ErrorMsg : Msg { public string Code; public string Text; }
    public sealed class ByeMsg : Msg { }
    public sealed class UnknownMsg : Msg { public string Type; }
}
