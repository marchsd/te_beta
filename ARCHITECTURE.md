# Architecture (provisional)

**Decision status:** the Terraria integration choice (TerrariaModder vs tModLoader 1.4.4 vs companion renderer)
is deliberately NOT made yet. It requires reading the real framework source and rendering backend, which is
Milestone 2. Do not treat any Terraria claim here as verified.

## Layers (independent, testable)
```
DesktopWorld.Windows  Win32 -> DesktopSnapshot   (IDesktopSource; Mock source for tests/dev)
DesktopWorld.Core     snapshot -> parts, tracker, WorldTransform, watcher   (platform neutral)
[M3] world model      stable-id platforms, mined-tile overlay, grace tombstones
[M4] protocol/bridge  versioned local named-pipe messages
[M2/M5] terraria-mod  chosen framework integration
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
