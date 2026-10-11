# Terraria: Desktop World

Goal: play a Terraria-style character on your real Windows desktop, with open windows as terrain.
**Current state: Milestone 1 only** (window detection and desktop geometry). There is no character,
overlay, or Terraria integration yet. See `docs/STATUS.md` for exactly what exists and what is next.

## What works now
- `DesktopWorld.Windows`: enumerates visible top-level windows via Win32/DWM (visible frame bounds, DPI,
  monitors, taskbar, maximized/fullscreen, z-order, cloaked/tool/click-through/own-process filtering).
  Geometry only: no titles, text, screenshots, or keystrokes are read.
- `DesktopWorld.Core`: window to platform decomposition (title bar, borders, interior), screen<->world
  transform (DPI-aware, 16 px tiles, multi-monitor negative coordinates), change tracker
  (added/removed/moved/resized/state/z-order), polling watcher, mock desktop, ASCII debug map.
- `deskworld` CLI: `list`, `watch [--map]`, `dump` (JSON). `--mock` works on any OS.

## Run (Windows 11, .NET 8 SDK)
```powershell
.\scripts\build.ps1                 # build + 24 tests
.\scripts\list.ps1                  # table of your real windows
.\scripts\watch.ps1                 # live events + ASCII map; drag a window and watch it update
.\scripts\watch.ps1 --mock          # fake desktop (no Windows needed)
dotnet run --project src/DesktopWorld.Cli -- dump > desktop.json
```
Logs: `%LOCALAPPDATA%\DesktopWorld\logs`.

## Verification status
Verified in a Linux sandbox: build, 24 unit tests, mock-mode CLI. **Not yet run on Windows**: the Win32 source
compiles but has not been exercised on a real desktop. Please run `list` and `watch` and report anything odd
(missing/extra windows, wrong rectangles at your DPI scaling).

## Known limitations (M1)
- Polling (default 50 ms), not WinEvent hooks. Secondary taskbars not detected.
- Occlusion (the visible part of overlapped windows) is not computed yet; z-order is reported.
- Minimized windows disappear (reported as Removed, return as Added with the same id).
