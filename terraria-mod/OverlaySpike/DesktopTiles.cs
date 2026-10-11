using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaModder.Core.Logging;

namespace OverlaySpike
{
    /// <summary>
    /// PROTOTYPE tile mirror. Reads real window rectangles (geometry only; class name read transiently for filtering,
    /// never stored; no titles/contents) and writes solid Terraria tiles for title bar + borders. Interiors stay air.
    /// Screen pixel (0,0) maps to world tile (OriginTileX, OriginTileY); 16 px = 1 tile.
    /// Temporary shortcut: the mod reads windows itself. The companion + pipe path comes later.
    /// </summary>
    internal sealed class DesktopTiles
    {
        private const int Ts = 16;
        private const int TitleRows = 2;      // 32 px
        private readonly ILogger _log;

        public int OriginTileX, OriginTileY, GridW, GridH;
        public Vector2 CameraPx { get { return new Vector2(OriginTileX * (float)Ts, OriginTileY * (float)Ts); } }
        public bool Running { get; private set; }

        private struct Cell { public long Owner; public int OffX, OffY; }
        private readonly Dictionary<long, Cell> _applied = new Dictionary<long, Cell>();
        private readonly Dictionary<long, HashSet<long>> _mined = new Dictionary<long, HashSet<long>>();
        private readonly Dictionary<long, int> _lastSeen = new Dictionary<long, int>();
        private int _tick;
        private int _frames;
        private readonly List<Rectangle> _interiors = new List<Rectangle>();   // screen px, rebuilt on every Sync
        private bool _spawnFlip;
        private int _floorRow;
        private int _stuck;
        private int _lastWinCount = -1;

        public DesktopTiles(ILogger log) { _log = log; }

        private static long Key(int x, int y) { return ((long)(x + 4096) << 24) | (long)(y + 4096); }
        private static long OffKey(int x, int y) { return ((long)x << 24) | (long)y; }

        // ---------------- start / stop ----------------
        public bool Start()
        {
            if (Main.gameMenu || Main.LocalPlayer == null) { _log.Warn("Enter a world first."); return false; }
            GridW = (Main.screenWidth + Ts - 1) / Ts;
            GridH = (Main.screenHeight + Ts - 1) / Ts;
            OriginTileX = Main.maxTilesX / 2 - GridW / 2;
            OriginTileY = Math.Max(60, (int)Main.worldSurface - GridH - 40);
            _log.Info("DesktopTiles: screen " + Main.screenWidth + "x" + Main.screenHeight + " -> grid " + GridW + "x" + GridH
                + ", origin tile (" + OriginTileX + "," + OriginTileY + "), worldSurface " + (int)Main.worldSurface);

            // Floor = top of the Windows taskbar (work area bottom), so the character is standing on screen, not below it.
            RECT wa = new RECT(); int bottom = Main.screenHeight;
            try { if (SystemParametersInfo(0x30, 0, ref wa, 0) && wa.B > 0) bottom = wa.B; } catch { }
            _floorRow = Math.Max(4, Math.Min(GridH - 1, FloorDiv(bottom, Ts)));
            _log.Info("DesktopTiles: floor row " + _floorRow + " (work area bottom " + bottom + " px)");

            int mw = GetSystemMetrics(0), mh = GetSystemMetrics(1);
            if (mw != Main.screenWidth || mh != Main.screenHeight)
                _log.Warn("DesktopTiles: Terraria client is " + Main.screenWidth + "x" + Main.screenHeight + " but the primary monitor is " + mw + "x" + mh
                    + ". Window terrain will be misaligned. Set Terraria to windowed at the monitor's resolution (and 100% display scaling).");
            else _log.Info("DesktopTiles: Terraria client matches the primary monitor (" + mw + "x" + mh + ").");

            WipeRegion();
            _applied.Clear(); _mined.Clear(); _lastSeen.Clear(); _tick = 0;
            Running = true;
            Sync();
            SpawnPlayer();
            return true;
        }

        public void Stop() { Running = false; }

        private void WipeRegion()
        {
            int x0 = Math.Max(5, OriginTileX - 12), x1 = Math.Min(Main.maxTilesX - 5, OriginTileX + GridW + 12);
            int y0 = Math.Max(5, OriginTileY - 25), y1 = Math.Min(Main.maxTilesY - 5, OriginTileY + GridH + 12);
            int n = 0;
            for (int x = x0; x < x1; x++)
                for (int y = y0; y < y1; y++)
                {
                    var t = Main.tile[x, y];
                    if (t == null) continue;
                    t.active(false);
                    t.liquid = 0;
                    t.wall = 0;
                    t.slope(0);
                    n++;
                }
            _log.Info("DesktopTiles: cleared " + n + " tiles to air (x " + x0 + ".." + x1 + ", y " + y0 + ".." + y1 + ")");
        }

        private void SpawnPlayer()
        {
            Player p = Main.LocalPlayer;
            Vector2 v;
            if (FindFreeSpot(p, out v)) p.position = v;
            else p.position = new Vector2(CameraPx.X + Main.screenWidth / 2f, CameraPx.Y + 40f);
            p.velocity = Vector2.Zero;
            p.fallStart = (int)(p.position.Y / Ts);
            _log.Info("DesktopTiles: player placed at screen px (" + (int)(p.position.X - CameraPx.X) + "," + (int)(p.position.Y - CameraPx.Y) + ")");
        }

        // First spot (scanning down from the top, middle of the screen outwards) where the player's hitbox overlaps no solid tile.
        private bool FindFreeSpot(Player p, out Vector2 pos)
        {
            int w = p.width, h = p.height;
            int[] dxs = { 0, -120, 120, -240, 240, -480, 480, -720, 720 };
            for (int k = 0; k < dxs.Length; k++)
                for (int sy = 8; sy < Main.screenHeight - h - 8; sy += 8)
                {
                    Vector2 v = new Vector2(CameraPx.X + Main.screenWidth / 2f + dxs[k] - w / 2f, CameraPx.Y + sy);
                    if (!Collision.SolidCollision(v, w, h)) { pos = v; return true; }
                }
            pos = Vector2.Zero;
            return false;
        }

        // ---------------- per-frame ----------------
        public void OnFrame()
        {
            if (!Running) return;
            Main.screenPosition = CameraPx;
        }

        public void Tick()
        {
            if (!Running) return;
            Player p = Main.LocalPlayer;
            if (p != null && p.velocity.Y > 0f) p.fallStart = (int)(p.position.Y / Ts);   // no fall damage on this toy
            try { AxeSwing(p); } catch (Exception ex) { if (!_axeErr) { _axeErr = true; _log.Error("AxeSwing failed: " + ex.Message); } }
            try
            {
                // Rescue: a window dragged over the player (or a bad spawn) must never leave them stuck inside solid tiles.
                if (p != null && Collision.SolidCollision(p.position, p.width, p.height))
                {
                    if (++_stuck >= 20)
                    {
                        _stuck = 0;
                        Vector2 v;
                        if (FindFreeSpot(p, out v)) { p.position = v; p.velocity = Vector2.Zero; _log.Info("Rescue: player was inside window tiles, moved out."); }
                    }
                }
                else _stuck = 0;
            }
            catch (Exception ex) { if (!_rescueErr) { _rescueErr = true; _log.Error("Rescue failed: " + ex.Message); } }
            if (++_frames % 4 != 0) return;
            try { Sync(); } catch (Exception ex) { _log.Error("DesktopTiles.Sync failed: " + ex); }
        }

        // Window tiles are one-way PLATFORMS (tile type 19): stand on them, jump up through them from below (W / Space), hold S to drop
        // through. No flying. Breaking: pickaxes go through the KillTile hook (instant, no drop); axes can't mine non-wood tiles in
        // vanilla, so a swing with an axe breaks the window tile under the cursor here.
        private bool _axeErr, _rescueErr;
        private int _prevAnim;

        public bool IsWindowTile(int wx, int wy)
        {
            if (!Running) return false;
            Cell c;
            return _applied.TryGetValue(Key(wx - OriginTileX, wy - OriginTileY), out c) && c.Owner != 0;
        }

        private void AxeSwing(Player p)
        {
            if (p == null) return;
            int anim = p.itemAnimation;
            bool newSwing = anim > _prevAnim;      // animation counter jumps up when a swing starts
            _prevAnim = anim;
            if (!newSwing) return;
            Item it = p.HeldItem;
            if (it == null || it.axe <= 0) return;
            int tx = Player.tileTargetX, ty = Player.tileTargetY;
            if (!IsWindowTile(tx, ty)) return;
            float dx = (tx * Ts + 8f) - p.Center.X, dy = (ty * Ts + 8f) - p.Center.Y;
            if (dx * dx + dy * dy > 112f * 112f) return;
            WorldGen.KillTile(tx, ty, false, false, true);
        }

        // Spawns a Zombie (NPC type 2) beside the player, inside the screen. NPC.NewNPC is called by reflection so the
        // exact 1.4.5 overload is discovered, not guessed; if it can't be found this logs why and does nothing.
        public void SpawnEnemy()
        {
            Player p = Main.LocalPlayer;
            if (p == null) return;
            _spawnFlip = !_spawnFlip;
            float x = p.Center.X + (_spawnFlip ? 240f : -240f);
            float minX = CameraPx.X + 48f, maxX = CameraPx.X + Main.screenWidth - 48f;
            if (x < minX) x = minX; if (x > maxX) x = maxX;
            int ix = (int)x, iy = (int)(p.Center.Y - 40f);

            MethodInfo best = null;
            foreach (MethodInfo m in typeof(NPC).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "NewNPC") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length >= 4 && ps[0].ParameterType.Name == "IEntitySource") { best = m; break; }
            }
            if (best == null) { _log.Warn("SpawnEnemy: NPC.NewNPC(IEntitySource, ...) overload not found."); return; }

            ParameterInfo[] pars = best.GetParameters();
            object[] args = new object[pars.Length];
            int ints = 0;
            for (int i = 0; i < pars.Length; i++)
            {
                Type pt = pars[i].ParameterType;
                if (i == 0) args[i] = null;
                else if (pt == typeof(int))
                {
                    ints++;
                    if (ints == 1) args[i] = ix;
                    else if (ints == 2) args[i] = iy;
                    else if (ints == 3) args[i] = 2;       // Zombie
                    else args[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : 0;
                }
                else if (pars[i].HasDefaultValue) args[i] = pars[i].DefaultValue;
                else args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
            object r = best.Invoke(null, args);
            _log.Info("SpawnEnemy: NPC.NewNPC returned " + r + " at pixel (" + ix + "," + iy + ")");
        }

        // ---------------- sync ----------------
        private void Sync()
        {
            _tick++;
            List<WinRect> wins = Enumerate();

            // 1) notice mined tiles (a tile we placed is no longer active) and remember per window
            var gone = new List<long>();
            foreach (KeyValuePair<long, Cell> kv in _applied)
            {
                int sx, sy; Unkey(kv.Key, out sx, out sy);
                var t = Main.tile[OriginTileX + sx, OriginTileY + sy];
                if (t != null && !t.active())
                {
                    if (kv.Value.Owner != 0)
                    {
                        HashSet<long> set;
                        if (!_mined.TryGetValue(kv.Value.Owner, out set)) { set = new HashSet<long>(); _mined[kv.Value.Owner] = set; }
                        set.Add(OffKey(kv.Value.OffX, kv.Value.OffY));
                    }
                    gone.Add(kv.Key);
                }
            }
            foreach (long k in gone) _applied.Remove(k);

            // 2) desired solid cells
            var desired = new Dictionary<long, Cell>();
            _interiors.Clear();
            for (int wi = 0; wi < wins.Count; wi++)
            {
                WinRect w = wins[wi];
                _lastSeen[w.Hwnd] = _tick;
                HashSet<long> mined; _mined.TryGetValue(w.Hwnd, out mined);
                int cx0 = FloorDiv(w.L, Ts), cx1 = CeilDiv(w.R, Ts) - 1, cy0 = FloorDiv(w.T, Ts), cy1 = CeilDiv(w.B, Ts) - 1;
                int cw = cx1 - cx0 + 1, ch = cy1 - cy0 + 1;
                if (cw < 3 || ch < 3) continue;
                for (int ox = 0; ox < cw; ox++)
                    for (int oy = 0; oy < ch; oy++)
                    {
                        // one-tile platform outline: top edge (title bar), bottom edge, left and right edges
                        bool solid = oy == 0 || oy == ch - 1 || ox == 0 || ox == cw - 1;
                        if (!solid) continue;
                        int sx = cx0 + ox, sy = cy0 + oy;
                        if (sx < 0 || sy < 0 || sx >= GridW || sy >= GridH) continue;
                        if (mined != null && mined.Contains(OffKey(ox, oy))) continue;
                        // occlusion: a cell hidden behind a window that is higher in the z-order is not terrain
                        int px = sx * Ts + Ts / 2, py = sy * Ts + Ts / 2;
                        bool hidden = false;
                        for (int a = 0; a < wi; a++)
                            if (px >= wins[a].L && px < wins[a].R && py >= wins[a].T && py < wins[a].B) { hidden = true; break; }
                        if (hidden) continue;
                        long key = Key(sx, sy);
                        if (!desired.ContainsKey(key)) desired[key] = new Cell { Owner = w.Hwnd, OffX = ox, OffY = oy };
                    }
            }
            // invisible screen boundary: side walls and a floor below the screen
            for (int y = -2; y <= _floorRow + 1; y++) { desired[Key(-1, y)] = new Cell(); desired[Key(GridW, y)] = new Cell(); }
            for (int x = -1; x <= GridW; x++) desired[Key(x, _floorRow)] = new Cell();

            if (wins.Count != _lastWinCount) { _lastWinCount = wins.Count; _log.Info("DesktopTiles: " + wins.Count + " window(s) mirrored as one-way platforms (occluded parts skipped)."); }

            // 3) apply diff
            var remove = new List<long>();
            foreach (KeyValuePair<long, Cell> kv in _applied)
                if (!desired.ContainsKey(kv.Key) || desired[kv.Key].Owner != kv.Value.Owner || desired[kv.Key].OffX != kv.Value.OffX || desired[kv.Key].OffY != kv.Value.OffY)
                    remove.Add(kv.Key);
            foreach (long k in remove) { int sx, sy; Unkey(k, out sx, out sy); SetTile(sx, sy, false, 0); _applied.Remove(k); }
            foreach (KeyValuePair<long, Cell> kv in desired)
                if (!_applied.ContainsKey(kv.Key)) { int sx, sy; Unkey(kv.Key, out sx, out sy); SetTile(sx, sy, true, kv.Value.Owner == 0 ? (ushort)1 : (ushort)19); _applied[kv.Key] = kv.Value; }

            // 4) forget mined holes of windows gone for ~1 minute
            var drop = new List<long>();
            foreach (KeyValuePair<long, int> kv in _lastSeen) if (_tick - kv.Value > 900) drop.Add(kv.Key);
            foreach (long h in drop) { _lastSeen.Remove(h); _mined.Remove(h); }
        }

        private void SetTile(int sx, int sy, bool solid, ushort type)
        {
            int wx = OriginTileX + sx, wy = OriginTileY + sy;
            if (wx < 1 || wy < 1 || wx >= Main.maxTilesX - 1 || wy >= Main.maxTilesY - 1) return;
            var t = Main.tile[wx, wy];
            if (t == null) return;
            if (solid) { t.type = type; t.frameX = 0; t.frameY = 0; t.active(true); t.slope(0); t.halfBrick(false); t.liquid = 0; }
            else { t.active(false); }
        }

        private static void Unkey(long k, out int x, out int y) { x = (int)(k >> 24) - 4096; y = (int)(k & 0xFFFFFF) - 4096; }
        private static int FloorDiv(int a, int b) { return (int)Math.Floor(a / (double)b); }
        private static int CeilDiv(int a, int b) { return (int)Math.Ceiling(a / (double)b); }

        // ---------------- window enumeration (geometry only) ----------------
        private struct WinRect { public long Hwnd; public int L, T, R, B; }
        private delegate bool EnumProc(IntPtr h, IntPtr lp);

        private List<WinRect> Enumerate()
        {
            var list = new List<WinRect>();
            uint self = (uint)Process.GetCurrentProcess().Id;
            var cls = new StringBuilder(64);
            EnumWindows(delegate (IntPtr h, IntPtr lp)
            {
                if (list.Count >= 25) return false;
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (pid == self) return true;
                int cloaked = 0;
                if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                int ex = GetWindowLong(h, -20);
                if ((ex & 0x80) != 0 && (ex & 0x40000) == 0) return true;     // WS_EX_TOOLWINDOW without APPWINDOW
                cls.Length = 0; GetClassName(h, cls, cls.Capacity);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Windows.UI.Core.CoreWindow") return true;
                RECT r;
                if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) { if (!GetWindowRect(h, out r)) return true; }
                if (r.R - r.L < 48 || r.B - r.T < 48) return true;
                list.Add(new WinRect { Hwnd = h.ToInt64(), L = r.L, T = r.T, R = r.R, B = r.B });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, ref RECT rect, uint winIni);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr lp);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT v, int size);
    }
}
