using System.Runtime.InteropServices;
using System.Text;

namespace DesktopWorld.Windows;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

internal static class Native
{
    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    public delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint data);

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000,
                      WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint h);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint h, int index);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(nint h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowW(string? cls, string? title);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc cb, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(nint hMon, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(nint ctx);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint hMon, int type, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] public static extern int DwmGetRect(nint h, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] public static extern int DwmGetInt(nint h, int attr, out int value, int size);
}
