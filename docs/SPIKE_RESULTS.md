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
2. Press **F8**: three strips appear. Left = A (alpha 0), middle = B (magenta), right = C (white).
3. Press **F7** repeatedly. After each press, look at the screen and note what you see.
   - Mode 1 Borderless: window loses its title bar. Expected: strips look like plain colors.
   - Mode 2 ColorKey: **the magenta strip B should show your desktop/windows through it.**
   - Mode 3 DwmAlpha: **strip A (and maybe everything black) may become see-through.**
   - 4th press returns to normal.
4. Press **F9** (with mode 2 on): can you click windows through the see-through part?
5. If the game gets stuck, press F7 until it returns to Off, or just close it.

## Report back
- For each mode (1, 2, 3): which strips were see-through (A / B / C / none), any flicker, black boxes, crashes.
- Does click-through (F9) work?
- Paste the last ~30 lines of `<Terraria>\TerrariaModder\core\logs\` newest file (has the HRESULT and True/False results).
- Screenshot of mode 2 and mode 3 if you can.

## Interpreting results (for me)
| Result | Next |
|---|---|
| B see-through (ColorKey) | Use colorkey; reserve magenta, hide vanilla tile drawing, continue to tile mirror |
| A see-through (DwmAlpha) | Use real alpha; better edges |
| Neither | Fallback: companion-owned overlay renderer fed by the mod |
