# Overlay spike: what to do and what to report

**Goal:** find out if Terraria's window can show your desktop through it. This decides the whole project
(if not, we switch to a fallback renderer). Takes about 10 minutes.

## Setup (once)
1. In Terraria: Settings > Video, set **Windowed** (not fullscreen), resolution **1920x1080**.
2. You need the .NET SDK (you have it) and the **.NET Framework 4.8 Developer Pack**
   (https://dotnet.microsoft.com/download/dotnet-framework/net48).
3. In PowerShell from the repo root:
   `.\scripts\build-spike.ps1`
   (if Terraria isn't in the default Steam path: `.\scripts\build-spike.ps1 -TerrariaDir "D:\...\Terraria"`)
   If the build fails, copy me the error. That is expected on a first try against a real install.

## Run
1. Launch Terraria with **TerrariaInjector.exe** (not Terraria.exe). Enter any world.
2. Press **NumPad8**: three strips appear. Left = A (alpha 0), middle = B (magenta), right = C (white).
3. Press **NumPad7** repeatedly. After each press, look at the screen and note what you see.
   - Mode 1 Borderless: window loses its title bar. Expected: strips look like plain colors.
   - Mode 2 ColorKey: **the magenta strip B should show your desktop/windows through it.**
   - Mode 3 DwmAlpha: **strip A (and maybe everything black) may become see-through.**
   - 4th press returns to normal.
4. Press **NumPad9** (with mode 2 on): can you click windows through the see-through part?
5. If the game gets stuck, press NumPad7 until it returns to Off, or just close it.

## Report back
- For each mode (1, 2, 3): which strips were see-through (A / B / C / none), any flicker, black boxes, crashes.
- Does click-through (NumPad9) work?
- Paste the last ~30 lines of `<Terraria>\TerrariaModder\core\logs\` newest file (has the HRESULT and True/False results).
- Screenshot of mode 2 and mode 3 if you can.

## Interpreting results (for me)
| Result | Next |
|---|---|
| B see-through (ColorKey) | Use colorkey; reserve magenta, hide vanilla tile drawing, continue to tile mirror |
| A see-through (DwmAlpha) | Use real alpha; better edges |
| Neither | Fallback: companion-owned overlay renderer fed by the mod |

## Result so far (from your screenshot)
Window mode 3 (DwmAlpha) worked: the alpha-0 strip showed your desktop through Terraria. Real transparency is possible.

## Step 2: dump Terraria's draw methods (so I can remove tiles/backgrounds)
1. Rebuild/install: `.\scripts\build-spike.ps1`, launch with TerrariaInjector, enter a world.
2. Press **NumPad0** once. Wait a second.
3. Send me `OverlaySpike-dump.txt` from your Terraria folder (same folder as Terraria.exe).
Note: the keys are now NumPad7/8/9/0 because F8 is Terraria's own packet-stats key. Turn NumLock on.

## Step 3 (v0.3): hide tiles + backgrounds
The dump gave the real method names (Main.DrawBG, DrawBackground, DoDraw_Tiles_Solid, DoDraw_WallsAndBlacks, ...).
1. Rebuild/install, launch with TerrariaInjector, enter a world (a surface world is fine).
2. Press **NumPad7 three times** (mode 3, DwmAlpha), then **NumPad1** (hide world).
3. Walk around. Expected: no sky/tiles/walls; your character, NPCs, and UI float over your real desktop.
4. Report: what you see (screenshot!), anything still drawn (leftover black, sky, tiles, water), any crash,
   plus the last ~40 lines of the newest log in `Terraria\TerrariaModder\core\logs\`
   (it lists which patches applied, e.g. "Skip-patch Main.DrawBG: 1 method(s)").
5. NumPad1 again turns the world back on.
Not yet in this build: tile mirror, W/S movement, companion link, input handling. This only tests rendering.

## Step 4 (v0.4): DESKTOP MODE (NumPad1)  <- the real prototype
**Use a throwaway world.** Create a NEW small world (any name) and a throwaway character. The mod empties part of the
world to air and blocks all world saving for the rest of the session (log line "Save-guard ... N method(s)");
if the guard can't be installed the mod refuses to start. Still: quit the game WITHOUT relying on the world surviving.

1. Rebuild (`.\scripts\build-spike.ps1`), launch with TerrariaInjector, enter the new world. Terraria windowed 1920x1080, NumLock on.
2. Open a few normal windows (browser, explorer...), un-maximized, so there are edges to stand on.
3. Press **NumPad1**. Expected: Terraria goes borderless/transparent, sky, tiles, minimap and health bars vanish
   (hotbar/inventory stay), your character drops onto the top of one of your windows.
4. A/D walk, Space jump. Left-click with the pickaxe (slot 2) on a window's title bar/border to mine it.
5. Drag a window: its terrain should follow within ~1/6 s.
6. NumPad1 again returns the window to normal. Click the desktop once and Terraria may pause (it loses focus). Click it to resume.
Known limits: no W/S climbing inside windows yet; windows hidden behind others still count as terrain (no occlusion);
mouse clicks go to Terraria while active (not click-through); 100% DPI scaling assumed.

Report: screenshot, what's wrong, and the log (look for lines starting "Skip-patch", "Camera-lock", "Save-guard",
"DesktopTiles", "Clear-patch").

## Step 5 (v0.4.1): use it
Build with `.\scripts\build-spike.ps1`, launch with TerrariaInjector, enter a THROWAWAY world, NumLock on, Terraria windowed 1920x1080 at 100% DPI.

| Key | Action |
|-----|--------|
| NumPad1 | Desktop mode on/off (transparent overlay, your windows become terrain) |
| A / D / Space | walk / jump (normal Terraria) |
| W / S | climb up / down while your character is inside a window's interior |
| Left click (pickaxe, slot 2) | mine a title bar / border tile |
| NumPad2 | spawn a Zombie next to you |
| **Ctrl+Alt+G** | works from anywhere: toggle GAME (Terraria takes input) <-> DESKTOP (click-through, your apps get the mouse; Terraria pauses when unfocused) |
| **Ctrl+Alt+Q** | emergency exit: normal window immediately, desktop mode off |

Send back from `Terraria\TerrariaModder\core\logs\` (newest file) every line containing: `Scene-target`, `WorldSceneLayerTarget members`,
`Scene targets:`, `Clear-patch`, `Global hotkeys`, `SpawnEnemy`, `Climb`, ERROR, WARN. Plus a screenshot.

### If you get stuck (v0.4.2)
Ctrl+Alt+Q = emergency exit from anywhere. Ctrl+Alt+G = click-through so your apps work. Alt+Tab still works; the overlay stays on top.
Report log lines containing: `Erase`, `Scene targets`, `floor row`, `player placed`, `Rescue`, ERROR, WARN.

### v0.4.3 report lines
`Draw-gate`, `Erase pass #1`, `Erase will also clear`, `Main static RenderTarget2D fields`, ERROR, WARN, plus a screenshot.
