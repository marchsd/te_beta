# Status and resume guide

## Milestones
| # | Scope | State |
|---|-------|-------|
| 1 | Window detection, geometry, transform, tracker, mock, debug map, tests | DONE (Linux-verified; needs one run on real Windows) |
| 2 | Research + decision | DONE for the decision (Option A, see ARCHITECTURE.md); transparency/render feasibility still UNVERIFIED, needs a spike on real Windows |
| 3 | World model: stable-id platforms, occlusion, tombstones, mined-tile overlay (+tests) | TODO |
| 4 | Versioned named-pipe protocol + heartbeats/reconnect (+tests) | TODO |
| 5 | Minimal playable prototype (player physics on geometry, W/S in interiors, mining) possibly with a stand-in renderer | TODO |
| 6 | Transparent click-through overlay, gameplay toggle, input ownership, Esc | TODO |
| 7 | Terraria mod integration: real player/enemies/spawning | TODO |

## First thing to do on resume
1. On Windows run `scripts\list.ps1` and `scripts\watch.ps1`; fix any Win32 filtering/rect issues found.
2. Start Milestone 2 research (this was NOT done: no framework claims are verified).

## Sandbox note
NuGet was unreachable in the authoring sandbox; builds used `-p:RestoreSources=<empty dir>`.

## Update (checkpoint 2)
M3 (world model), M4 (protocol + pipe bridge), M5 (headless sim) are DONE and tested on Linux (65 tests).
Overlay (M6) and Terraria mod (M7) are NOT done yet at this checkpoint.

## Update (checkpoint 3: M2 decision)
Chose TerrariaModder (1.4.5.8, net48, 32-bit XNA/D3D9) with a "tile mirror" design; tModLoader is still on 1.4.4.
Nothing was run against a real Terraria. Next: write a minimal net48 mod spike that (1) writes mirrored tiles,
(2) tries the overlay transparency options in ARCHITECTURE.md, and report which works. The sandbox has no Windows or
Terraria, so that spike must be run by you; build it here, test on your machine.

## Update (checkpoint 4)
`scripts\list.ps1` verified on real Windows 11 (7 windows, correct work-area and DPI 96). OverlaySpike mod written
(terraria-mod/OverlaySpike); compile-checked only against stub types, NOT against real Terraria assemblies, and not
run. Next: user runs docs/SPIKE_RESULTS.md and reports.

## Update (checkpoint 5)
Spike result: DwmAlpha transparency WORKS (user screenshot). Dump of 1.4.5.8 draw code received; patch targets taken from it.
OverlaySpike v0.3 adds WorldHider (Harmony skip of tile/wall/sky/bg draws + black clear -> transparent). Compile-checked
against stubs only; not yet run. Risk: 1.4.5 renders layers via WorldSceneLayerTarget render targets; leftovers possible.

## Update (checkpoint 6)
OverlaySpike v0.4: NumPad1 = desktop mode: save-guard, transparent window, camera lock, UI/sky/tile draw skipped,
region wiped to air, tile mirror from real windows (title 2 rows + 1-tile borders, interior air), per-window mined-hole
memory, boundary walls/floor, ~6 Hz resync. Prototype reads windows inside the mod (companion/pipe path deferred).
Compile-checked against stubs only; NOT run. Not done: W/S in interiors, occlusion, click-through, enemy spawning hotkey,
DPI-unaware coordinates, companion link.

## Update (checkpoint 7: v0.4.1, sky that would not go away)
Log + dump analysis: the 1-arg `GraphicsDevice.Clear(Color)` patch DID apply (only the 4-arg XNA overload fails, harmless).
The leftover sky is most likely cached `WorldSceneLayerTarget` render targets: `Main.DoDraw` blits their `Texture` inline at
8 call sites (not skippable by method), and `RenderToTargets` only refreshes them when the camera moves, which the camera lock
prevents. v0.4.1 patches `WorldSceneLayerTarget.UpdateContent` to clear each target to transparent while desktop mode is active.
NOT compiled against real assemblies and NOT run. The log prints "WorldSceneLayerTarget members: ..." and
"Scene targets: X of Y ... cleared" to confirm or refute this. Still open: apps are not click-through, Terraria pauses when
unfocused, no W/S, no occlusion, 100% DPI assumed.

## Update (checkpoint 8: v0.4.1 package)
Added (all compile-unchecked, run-untested; there was no C# toolchain in the authoring sandbox):
- `GlobalHotkeys.cs`: Ctrl+Alt+G game/desktop toggle and Ctrl+Alt+Q emergency exit via RegisterHotKey on a private thread, active only
  while desktop mode is on.
- W/S climbing in window interiors (`DesktopTiles.Climb`): sets vertical velocity while W/S held and the player centre is inside an interior rect.
- NumPad2 spawns a Zombie via reflection-discovered `NPC.NewNPC` overload.
- `Apply()` is now lock-protected (hotkey thread + game thread).
Still NOT done: occlusion (hidden windows still count as terrain), DPI != 100%, multi-monitor, companion/pipe link, forcing Terraria to keep
running while unfocused (deliberately not done: it would turn clicks on your apps into Terraria attacks).

## Update (checkpoint 9: v0.4.2, from the run of v0.4.1)
Log of v0.4.1: all patches applied, "Scene targets: 600 of 600 cleared" but the sky STILL showed, so the cached-target theory was not the
(whole) cause. And the player was stuck at the top: spawn was at screen y=20 px, i.e. inside the title-bar tiles of a (probably maximized)
window, and the old floor sat below the screen edge. v0.4.2:
- ERASE PASS: prefix on `Main.DoDraw_WallsTilesNPCs` does `GraphicsDevice.Clear(Color.Transparent)`, wiping everything drawn before the
  world entities regardless of which method drew it. Log line: "Erase pass ran".
- Spawn scans down from the top for a hitbox-sized free spot (`Collision.SolidCollision`); a rescue moves the player out if embedded >20 frames.
- Floor is now the top of the taskbar work area (SPI_GETWORKAREA), not below the screen.
Untested; no C# toolchain in the authoring sandbox.

## Update (checkpoint 10: v0.4.3)
Run of v0.4.2: erase pass and scene-target clear both ran, sky still visible; character spawns correctly and mining window tiles works
("Stone Block" pickup). So the sky is painted into a target the single `Clear` didn't reach (the dump shows DrawHorizon/stars/sun are drawn
BEFORE `FilterManager.BeginCapture`, i.e. into a different target than the entities). v0.4.3:
- DRAW GATE: after `RenderToTargets`/`SetBackColor` until the entity pass, every `SpriteBatch.Draw` and non-generic GraphicsDevice primitive draw is
  dropped; reset at the start/end of every `DoDraw`.
- ERASE PASS now clears the backbuffer, every `Main.screenTarget*` static RenderTarget2D, then restores and clears the bound target.
- Diagnostics: logs bound target sizes, number of draw calls the gate dropped, and all static RenderTarget2D field names in Main.
Untested; no C# toolchain in the authoring sandbox.

## Update (checkpoint 11: v0.4.4)
No C# toolchain or network in the authoring sandbox (again), so this is code-reviewed, NOT compiled and NOT run.
Added: (1) `Main.SetBackColor` postfix forces every sky/background tint to pure black (black is what DWM shows through);
(2) `FilterManager.EndCapture` prefix wipes the backbuffer before the world composite; (3) startup warning if the Terraria client
size differs from the primary monitor (window terrain would be misaligned); (4) log line with mirrored window count.
Report lines for v0.4.4: `Black-sky`, `EndCapture`, `DesktopTiles:`, `Draw-gate`, `Erase pass`, ERROR, WARN, plus a screenshot.

## Update (checkpoint 12: v0.4.5)
v0.4.4 RAN on real Windows: transparent overlay + window terrain + character work (user confirmed). Remaining bug: lighting.
Cause: v0.4.4 forced ColorOfTheSkies/bgColor/backColor black, and ambient light derives from them -> dark/see-through character.
v0.4.5: stop forcing those; add `Lighting.GetColor` postfix returning full brightness while desktop mode is active.
Not compiled here (no toolchain); report lines: `Full-bright`, `Black-sky`, ERROR, WARN.

## Update (checkpoint 13: v0.4.6)
v0.4.5 confirmed working by the user (log: Full-bright 4 overloads). Changes (not compiled here, no toolchain):
- Window tiles are now one-way platforms (tile 19), 1-tile outline per window; W/S flying removed. Vanilla jump-through and S-to-drop apply.
- Occlusion: cells hidden behind a higher-z window are not terrain. Sync every 4 frames (was 10), 25-window cap.
- Window tiles break instantly with a pickaxe (KillTile hook via __args, no item drop); axes break them via a swing hook.
- Boundary walls/floor remain stone (type 1), unbreakable by the hook.
- Custom "Window" tile type NOT attempted: TerrariaModder showed only item/texture registration in the log; tiles are invisible anyway.
