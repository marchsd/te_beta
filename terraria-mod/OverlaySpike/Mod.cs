using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TerrariaModder.Core;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Logging;

namespace OverlaySpike
{
    /// <summary>
    /// SPIKE. Answers one question: which Win32 technique (if any) makes Terraria's XNA/D3D9 window show the desktop
    /// through "empty" pixels. Nothing here is gameplay. Results are written to the TerrariaModder log.
    /// </summary>
    public class Mod : ModBase, IModLifecycle
    {
        private ILogger _log;
        private int _mode;              // 0 Off, 1 Borderless, 2 ColorKey, 3 DwmAlpha
        private bool _pattern;
        private bool _clickThrough;
        private bool _saved;
        private int _origStyle, _origEx, _origX, _origY, _origW, _origH;
        private SpriteBatch _sb;
        private Texture2D _px;
        private bool _drawErrorLogged;

        private static readonly string[] ModeNames = { "Off", "Borderless", "ColorKey(magenta)", "DwmAlpha" };

        public override void Initialize(ModContext context)
        {
            _log = context.Logger;
            context.RegisterKeybind("cycle_mode", "Cycle window mode", "Off > Borderless > ColorKey > DwmAlpha", "NumPad7", CycleMode);
            context.RegisterKeybind("toggle_pattern", "Toggle test pattern", "Show/hide test strips", "NumPad8", TogglePattern);
            context.RegisterKeybind("toggle_clickthrough", "Toggle click-through", "Clicks pass to windows below", "NumPad9", ToggleClickThrough);
            context.RegisterKeybind("dump_methods", "Dump draw methods", "Write OverlaySpike-dump.txt", "NumPad0", DoDump);
            context.RegisterKeybind("toggle_desktop", "Desktop mode", "Empty world + your windows as terrain + transparent overlay", "NumPad1", ToggleDesktop);
            FrameEvents.OnPostDraw += OnPostDraw;
            FrameEvents.OnPreUpdate += OnPreFrame;
            FrameEvents.OnPreDraw += OnPreFrame;
            FrameEvents.OnPostUpdate += OnPostUpdate;
            _log.Info("OverlaySpike ready. NumPad7 = cycle window mode, NumPad8 = test pattern, NumPad9 = click-through, NumPad0 = dump draw methods, NumPad1 = DESKTOP MODE.");
        }

        public void OnContentReady(ModContext context) { }
        public void OnWorldLoad() { }
        public void OnWorldUnload() { }

        public override void Unload()
        {
            FrameEvents.OnPostDraw -= OnPostDraw;
            FrameEvents.OnPreUpdate -= OnPreFrame;
            FrameEvents.OnPreDraw -= OnPreFrame;
            FrameEvents.OnPostUpdate -= OnPostUpdate;
            try { if (_desk) { _desk = false; _tiles?.Stop(); } } catch { }
            try { WorldHider.Uninstall(); } catch { }
            try { _mode = 0; _clickThrough = false; Apply(); } catch (Exception ex) { _log?.Warn("Unload restore failed: " + ex.Message); }
        }

        private void CycleMode() { _mode = (_mode + 1) % ModeNames.Length; SafeApply(); }
        private void TogglePattern() { _pattern = !_pattern; _log.Info("Test pattern: " + (_pattern ? "ON" : "OFF")); }
        private void ToggleClickThrough() { _clickThrough = !_clickThrough; SafeApply(); }

        private DesktopTiles _tiles;
        private bool _desk;

        private void OnPreFrame() { if (_desk && _tiles != null) _tiles.OnFrame(); }
        private void OnPostUpdate() { if (_desk && _tiles != null) _tiles.Tick(); }

        private void ToggleDesktop()
        {
            try
            {
                if (_desk)
                {
                    _desk = false;
                    WorldHider.Uninstall();
                    if (_tiles != null) _tiles.Stop();
                    _mode = 0; _clickThrough = false; Apply();
                    _log.Info("Desktop mode OFF. The world was modified in memory and will NOT be saved (save guard stays on). Quit without saving.");
                    return;
                }
                if (Main.gameMenu || Main.LocalPlayer == null) { _log.Warn("Enter a world first (use a throwaway world)."); return; }
                WorldHider.Install(_log);
                if (!WorldHider.SaveGuardInstalled) { _log.Error("REFUSING desktop mode: could not install the world-save guard, so your world could be overwritten."); return; }
                WorldHider.Wiped = true;               // from this moment on, saving is blocked until the game closes
                _mode = 3; Apply();                    // borderless + DWM alpha transparency (verified working in the spike)
                if (_tiles == null) _tiles = new DesktopTiles(_log);
                if (!_tiles.Start()) { _mode = 0; Apply(); return; }
                WorldHider.CameraPx = _tiles.CameraPx;
                WorldHider.CameraLock = true;
                WorldHider.Active = true;
                _desk = true;
                _log.Info("Desktop mode ON.");
            }
            catch (Exception ex) { _log.Error("ToggleDesktop failed: " + ex); }
        }

        private void DoDump()
        {
            try { _log.Info("Dump written to: " + Dump.WriteFile()); }
            catch (Exception ex) { _log.Error("Dump failed: " + ex); }
        }

        private void SafeApply()
        {
            try { Apply(); }
            catch (Exception ex) { _log.Error("Apply failed (mode " + _mode + "): " + ex); }
        }

        private void Apply()
        {
            IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
            if (hwnd == IntPtr.Zero) { _log.Warn("No main window handle yet."); return; }

            RECT wr; GetWindowRect(hwnd, out wr);
            RECT cr; GetClientRect(hwnd, out cr);
            if (!_saved)
            {
                _origStyle = GetWindowLong(hwnd, GWL_STYLE);
                _origEx = GetWindowLong(hwnd, GWL_EXSTYLE);
                _origX = wr.Left; _origY = wr.Top; _origW = wr.Right - wr.Left; _origH = wr.Bottom - wr.Top;
                _saved = true;
            }

            // Always start from the original state, then layer the requested mode on top.
            var m0 = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 0 };
            DwmExtendFrameIntoClientArea(hwnd, ref m0);
            SetWindowLong(hwnd, GWL_EXSTYLE, _origEx);
            SetWindowLong(hwnd, GWL_STYLE, _origStyle);
            SetWindowPos(hwnd, HWND_NOTOPMOST, _origX, _origY, _origW, _origH, SWP_FRAMECHANGED | SWP_NOACTIVATE);

            if (_mode >= 1)
            {
                int cw = cr.Right - cr.Left, ch = cr.Bottom - cr.Top;
                if (_mode == 1 || _mode == 2 || _mode == 3)
                {
                    SetWindowLong(hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, cw, ch, SWP_FRAMECHANGED | SWP_NOACTIVATE);
                }
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                if (_mode == 2)
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);
                    bool ok = SetLayeredWindowAttributes(hwnd, 0x00FF00FF, 255, LWA_COLORKEY);
                    _log.Info("SetLayeredWindowAttributes(colorkey) -> " + ok);
                }
                else if (_mode == 3)
                {
                    var m = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                    int hr = DwmExtendFrameIntoClientArea(hwnd, ref m);
                    _log.Info("DwmExtendFrameIntoClientArea(-1) HRESULT = 0x" + hr.ToString("X8"));
                }
            }

            if (_clickThrough)
            {
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                ex |= WS_EX_TRANSPARENT;
                if (_mode != 2) ex |= WS_EX_LAYERED;
                SetWindowLong(hwnd, GWL_EXSTYLE, ex);
                if (_mode != 2) SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
            }

            _log.Info("Window mode = " + ModeNames[_mode] + ", clickThrough = " + _clickThrough
                + ", client " + (cr.Right - cr.Left) + "x" + (cr.Bottom - cr.Top));
        }

        private void OnPostDraw()
        {
            if (!_pattern) return;
            try
            {
                GraphicsDevice gd = Main.instance.GraphicsDevice;
                if (gd == null) return;
                if (_px == null || _px.IsDisposed)
                {
                    _px = new Texture2D(gd, 1, 1);
                    _px.SetData(new[] { Color.White });
                }
                if (_sb == null || _sb.IsDisposed) _sb = new SpriteBatch(gd);

                int w = gd.Viewport.Width, h = gd.Viewport.Height, third = w / 3, top = h / 4, bh = h / 2;
                _sb.Begin(SpriteSortMode.Immediate, BlendState.Opaque);
                // A: alpha 0 written straight to the backbuffer. Only see-through if the backbuffer HAS alpha (DwmAlpha mode).
                _sb.Draw(_px, new Rectangle(0, top, third, bh), Color.Transparent);
                // B: magenta key color. See-through in ColorKey mode.
                _sb.Draw(_px, new Rectangle(third, top, third, bh), new Color(255, 0, 255, 255));
                // C: control, must always stay visible.
                _sb.Draw(_px, new Rectangle(third * 2, top, w - third * 2, bh), Color.White);
                _sb.End();
            }
            catch (Exception ex)
            {
                if (!_drawErrorLogged) { _drawErrorLogged = true; _log.Error("Test-pattern draw failed: " + ex); }
            }
        }

        #region Win32
        private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        private const int WS_POPUP = unchecked((int)0x80000000), WS_VISIBLE = 0x10000000;
        private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
        private const uint LWA_ALPHA = 0x2, LWA_COLORKEY = 0x1;
        private const uint SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte alpha, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS m);
        #endregion
    }
}
