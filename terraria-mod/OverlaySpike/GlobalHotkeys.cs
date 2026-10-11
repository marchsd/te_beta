using System;
using System.Runtime.InteropServices;
using System.Threading;
using TerrariaModder.Core.Logging;

namespace OverlaySpike
{
    /// <summary>
    /// System-wide hotkeys that work even when Terraria is NOT focused (needed because in desktop mode the overlay is
    /// click-through and Terraria pauses when it loses focus, so its own keybinds can't fire).
    /// Registered only while desktop mode is on, on a private message-pump thread. Ctrl+Alt+G = toggle game/desktop,
    /// Ctrl+Alt+Q = emergency exit (restores the normal window).
    /// </summary>
    internal sealed class GlobalHotkeys
    {
        private const int WM_HOTKEY = 0x0312, WM_QUIT = 0x0012;
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
        private const uint VK_G = 0x47, VK_Q = 0x51;

        private readonly ILogger _log;
        private readonly Action _toggle, _exit;
        private Thread _thread;
        private volatile uint _threadId;

        public GlobalHotkeys(ILogger log, Action toggle, Action exit) { _log = log; _toggle = toggle; _exit = exit; }

        public void Start()
        {
            if (_thread != null && _thread.IsAlive) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "OverlaySpike-hotkeys" };
            _thread.Start();
        }

        public void Stop()
        {
            uint id = _threadId;
            if (id != 0) PostThreadMessage(id, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        private void Run()
        {
            try
            {
                _threadId = GetCurrentThreadId();
                bool g = RegisterHotKey(IntPtr.Zero, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_G);
                bool q = RegisterHotKey(IntPtr.Zero, 2, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_Q);
                _log.Info("Global hotkeys: Ctrl+Alt+G (game/desktop toggle) registered=" + g + ", Ctrl+Alt+Q (emergency exit) registered=" + q);
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.message != WM_HOTKEY) continue;
                    try
                    {
                        int id = msg.wParam.ToInt32();
                        if (id == 1) _toggle();
                        else if (id == 2) _exit();
                    }
                    catch (Exception ex) { _log.Error("Hotkey handler failed: " + ex.Message); }
                }
            }
            catch (Exception ex) { _log.Error("Hotkey thread failed: " + ex.Message); }
            finally
            {
                try { UnregisterHotKey(IntPtr.Zero, 1); UnregisterHotKey(IntPtr.Zero, 2); } catch { }
                _threadId = 0;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }
}
