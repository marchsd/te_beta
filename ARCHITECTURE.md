# Architecture

**Decision status (Milestone 2): Option A chosen: TerrariaModder (Terraria 1.4.5.8) + this repo's companion.**
What was read vs. what is still unverified is called out explicitly below. Nothing about rendering has been run
against a real Terraria yet.

## What was verified (read from source/docs, Oct 2026)
- Terraria 1.4.5 shipped Jan 2026 (stable 1.4.5.x). **tModLoader is still on 1.4.4**; its 1.4.5 port is a
  contributor-only dev branch with a "many months" estimate (tModLoader issue #5070).
- TerrariaModder (Inidar1/terraria-modder, MIT) targets **Terraria 1.4.5.8**, loads **.NET Framework 4.8 (net48)**
  mods into the normal Steam process via TerrariaInjector, bundles **Harmony**, and references Terraria/XNA directly.
  Its Core csproj references the **32-bit XNA 4.0 GAC** assemblies, so the game is a 32-bit XNA/DirectX 9 process.
- Its `Game` helper exposes `GetTile`, `PlaceTile`, `IsTileSolid`, `MaxTilesX/Y`, `ScreenPosition`, `Players/NPCs`.
  Events: `FrameEvents`, `PlayerEvents`, `NPCEvents`, `WorldEvents`. Manual Harmony patches go in `OnContentReady`.
- Its DebugTools mod only shows/hides the game window (`ShowWindow`); **no existing code makes the window
  transparent**, so the overlay is our own work.

## Options considered
| | A: TerrariaModder + companion | B: tModLoader 1.4.4 | C: companion-only renderer |
|---|---|---|---|
| Matches current game (1.4.5) | yes | no (1.4.4) | n/a |
| Real Terraria physics/mining/NPCs | yes, via real tiles | yes | no (reimplemented) |
| Process/renderer | 32-bit XNA, D3D9, Win32 window | 64-bit FNA/SDL | ours |
| Transparency path | Win32 on XNA window (unverified) | SDL/GL window (harder) | easy |
| Effort | medium | medium-high | high, and not Terraria |

**Chosen: A.** Rejected B: wrong game version and a harder transparency story. **C is the fallback** if the spike
below fails; the `DesktopWorld.Core.Sim` headless sim already exists as a stand-in physics core.

## Core idea: the tile mirror
Terraria's physics, mining, and NPC AI all run against `Main.tile`. So instead of reimplementing them, the mod
**writes real solid tiles into a small flat world that mirror the desktop geometry**, at the transform already
defined here (16 world px = 1 tile, +Y down). Consequences:
- Player walking/jumping/falling, pickaxe mining, and enemy collision are native Terraria behaviour.
- Window borders/title bars = solid tiles; interiors = empty tiles (the W/S mechanic below).
- Mining a tile raises a tile-kill event; the mod sends `MineMsg` and the companion records the hole in the
  window-local mined-tile overlay (already built and tested in `WorldModel`).
- A moved/resized window means a diff of tile rects from `GeometryMsg`, not a full rebuild.
- Real window pixel size / 16 gives the tile grid (1920x1080 -> 120x67 tiles), so the world is tiny.

## Still unverified (each is a spike, in this order)
1. **Transparent overlay on XNA/D3D9.** Terraria's window must show the desktop through empty pixels. Candidates,
   cheapest first: (a) `DwmExtendFrameIntoClientArea(-1)` + clear to alpha 0, which only works if the XNA backbuffer
   is `A8R8G8B8` and DWM honors it; (b) `WS_EX_LAYERED` + `LWA_COLORKEY` with a reserved key color (some game
   pixels will need to avoid it); (c) fall back to Option C's own overlay fed by mod state. Click-through is
   `WS_EX_TRANSPARENT`, toggled with gameplay mode. **Needs a real Windows + Terraria run to settle.**
2. **Hiding vanilla tile/background drawing** so only the character/NPCs/projectiles are visible over the desktop
   (Harmony patches on the tile and background draw paths; method names to be read from the decompiled
   assembly, not guessed).
3. **Camera lock** so 1 screen pixel = 1 world pixel at zoom 1 with the window covering the virtual screen.
4. **W/S inside interiors**: plan is a player-update postfix that, while the player's hitbox is inside an
   interior rect, suspends gravity and maps W/S to vertical velocity. Not yet written.
5. **Input ownership**: gameplay mode on = the overlay takes input; off = `WS_EX_TRANSPARENT`/hidden so the
   desktop works normally. Esc leaves gameplay mode. Release all held keys on pause/focus loss.

## Boundaries (what is Terraria vs ours)
- Terraria (real): player, animation, physics, collision, mining, items, NPCs, projectiles, damage, sounds.
- Ours: desktop detection, geometry to tile mirror, mined-hole persistence per window, overlay window style,
  input ownership, protocol, mock/debug tooling.
- Assets are never copied; they come from the installed game.

## Layers (independent, testable)
```
DesktopWorld.Windows  Win32 -> DesktopSnapshot   (IDesktopSource; Mock source for tests/dev)
DesktopWorld.Core     snapshot -> parts, tracker, WorldTransform, watcher   (platform neutral)
[M3] world model      stable-id platforms, mined-tile overlay, grace tombstones
[M4] protocol/bridge  versioned local named-pipe messages
[M7] terraria-mod  TerrariaModder (net48) tile mirror + overlay
[M6] overlay          transparent topmost click-through renderer, input ownership
```

## Decisions already made (M1)
- **Units:** physical pixels, virtual-screen coordinates (negative allowed); process is Per-Monitor-V2 aware.
- **World:** world = (screen - origin) / (primaryDpi/96); 16 world px per tile; +Y down (Terraria convention).
- **Window ids:** HWND value; stable for a window's life.
- **Parts:** title bar and 3 borders solid, interior traversable; parts exactly tile the window (tested).
  Border thickness (6 px at 96 DPI) is a gameplay value, not the OS metric. Maximized: no side/bottom
  borders. Fullscreen: all interior (to be excluded from solid terrain).
- **Privacy:** geometry only; the class name is read transiently for filtering and never stored.
- **Mining (planned):** terrain edits are stored in the window's local coordinates keyed by window id, so they
  move with the window; on resize edits are clamped/clipped. Never touches the real window.
