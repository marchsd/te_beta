using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            p.position = new Vector2(CameraPx.X + Main.screenWidth / 2f, CameraPx.Y + 20f);
            p.velocity = Vector2.Zero;
            p.fallStart = (int)(p.position.Y / Ts);
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
            if (++_frames % 10 != 0) return;
            try { Sync(); } catch (Exception ex) { _log.Error("DesktopTiles.Sync failed: " + ex); }
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
            foreach (WinRect w in wins)
            {
                _lastSeen[w.Hwnd] = _tick;
                HashSet<long> mined; _mined.TryGetValue(w.Hwnd, out mined);
                int cx0 = FloorDiv(w.L, Ts), cx1 = CeilDiv(w.R, Ts) - 1, cy0 = FloorDiv(w.T, Ts), cy1 = CeilDiv(w.B, Ts) - 1;
                int cw = cx1 - cx0 + 1, ch = cy1 - cy0 + 1;
                if (cw < 3 || ch < TitleRows + 2) continue;
                for (int ox = 0; ox < cw; ox++)
                    for (int oy = 0; oy < ch; oy++)
                    {
                        bool solid = oy < TitleRows || oy == ch - 1 || ox == 0 || ox == cw - 1;
                        if (!solid) continue;
                        int sx = cx0 + ox, sy = cy0 + oy;
                        if (sx < 0 || sy < 0 || sx >= GridW || sy >= GridH) continue;
                        if (mined != null && mined.Contains(OffKey(ox, oy))) continue;
                        long key = Key(sx, sy);
                        if (!desired.ContainsKey(key)) desired[key] = new Cell { Owner = w.Hwnd, OffX = ox, OffY = oy };
                    }
            }
            // invisible screen boundary: side walls and a floor below the screen
            for (int y = -2; y <= GridH + 1; y++) { desired[Key(-1, y)] = new Cell(); desired[Key(GridW, y)] = new Cell(); }
            for (int x = -1; x <= GridW; x++) desired[Key(x, GridH + 1)] = new Cell();

            // 3) apply diff
            var remove = new List<long>();
            foreach (KeyValuePair<long, Cell> kv in _applied)
                if (!desired.ContainsKey(kv.Key) || desired[kv.Key].Owner != kv.Value.Owner || desired[kv.Key].OffX != kv.Value.OffX || desired[kv.Key].OffY != kv.Value.OffY)
                    remove.Add(kv.Key);
            foreach (long k in remove) { int sx, sy; Unkey(k, out sx, out sy); SetTile(sx, sy, false); _applied.Remove(k); }
            foreach (KeyValuePair<long, Cell> kv in desired)
                if (!_applied.ContainsKey(kv.Key)) { int sx, sy; Unkey(kv.Key, out sx, out sy); SetTile(sx, sy, true); _applied[kv.Key] = kv.Value; }

            // 4) forget mined holes of windows gone for ~1 minute
            var drop = new List<long>();
            foreach (KeyValuePair<long, int> kv in _lastSeen) if (_tick - kv.Value > 360) drop.Add(kv.Key);
            foreach (long h in drop) { _lastSeen.Remove(h); _mined.Remove(h); }
        }

        private void SetTile(int sx, int sy, bool solid)
        {
            int wx = OriginTileX + sx, wy = OriginTileY + sy;
            if (wx < 1 || wy < 1 || wx >= Main.maxTilesX - 1 || wy >= Main.maxTilesY - 1) return;
            var t = Main.tile[wx, wy];
            if (t == null) return;
            if (solid) { t.type = (ushort)1; t.active(true); t.slope(0); t.liquid = 0; }
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
                if (list.Count >= 40) return false;
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
