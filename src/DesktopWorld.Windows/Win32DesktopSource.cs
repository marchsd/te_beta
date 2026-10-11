using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using DesktopWorld.Core;

namespace DesktopWorld.Windows;

public sealed class Win32SourceOptions
{
    public int MinWidth { get; init; } = 48;
    public int MinHeight { get; init; } = 32;
    /// <summary>Process ids never reported (the companion and its overlay add themselves here).</summary>
    public ISet<int> ExcludedProcessIds { get; init; } = new HashSet<int> { Environment.ProcessId };
}

public static class DpiAwareness
{
    /// <summary>Per-Monitor-V2 so every rectangle below is in true physical pixels. Call before any window is created.</summary>
    [SupportedOSPlatform("windows")]
    public static bool Enable() => Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
}

/// <summary>Reads real top-level window geometry via Win32/DWM. Collects NO titles, text or window content.
/// The class name is read only transiently to filter shell windows and is never stored.</summary>
[SupportedOSPlatform("windows")]
public sealed class Win32DesktopSource : IDesktopSource
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"
    };

    private readonly Win32SourceOptions _opt;
    public Win32DesktopSource(Win32SourceOptions? options = null) => _opt = options ?? new();

    public DesktopSnapshot Capture()
    {
        var monitors = EnumMonitors();
        var raw = new List<(nint h, uint pid, RECT r, bool zoomed)>();

        Native.EnumWindows((h, _) =>
        {
            if (TryReadWindow(h, out var pid, out var r, out var zoomed)) raw.Add((h, pid, r, zoomed));
            return true;
        }, 0);

        // EnumWindows enumerates top-to-bottom in z-order.
        var windows = new List<WindowInfo>(raw.Count);
        foreach (var (h, pid, r, zoomed) in raw)
        {
            var bounds = new RectI(r.Left, r.Top, r.Right, r.Bottom);
            int mi = MonitorIndexFor(monitors, bounds);
            var mon = monitors.Count > 0 ? monitors[mi] : new MonitorInfo(0, bounds, bounds, 96, true);
            var state = zoomed ? WindowState.Maximized
                      : (bounds.Left <= mon.Bounds.Left && bounds.Top <= mon.Bounds.Top &&
                         bounds.Right >= mon.Bounds.Right && bounds.Bottom >= mon.Bounds.Bottom) ? WindowState.Fullscreen
                      : WindowState.Normal;
            windows.Add(new WindowInfo(h, (int)pid, bounds, state, windows.Count, mi, mon.Dpi));
        }

        return new DesktopSnapshot(monitors, windows, ReadTaskbar(monitors), Environment.TickCount64);
    }

    private bool TryReadWindow(nint h, out uint pid, out RECT r, out bool zoomed)
    {
        pid = 0; r = default; zoomed = false;
        if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return false;
        if (Native.DwmGetInt(h, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;

        long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        bool appWindow = (ex & Native.WS_EX_APPWINDOW) != 0;
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;
        if ((ex & Native.WS_EX_NOACTIVATE) != 0) return false;
        if ((ex & Native.WS_EX_TRANSPARENT) != 0 && (ex & Native.WS_EX_LAYERED) != 0) return false; // click-through overlays

        Native.GetWindowThreadProcessId(h, out pid);
        if (_opt.ExcludedProcessIds.Contains((int)pid)) return false;

        var sb = new StringBuilder(256);
        if (Native.GetClassNameW(h, sb, sb.Capacity) > 0 && ShellClasses.Contains(sb.ToString())) return false;

        // Prefer the DWM visible frame: GetWindowRect includes invisible resize borders on Windows 10/11.
        if (Native.DwmGetRect(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>()) != 0 &&
            !Native.GetWindowRect(h, out r)) return false;

        if (r.Right - r.Left < _opt.MinWidth || r.Bottom - r.Top < _opt.MinHeight) return false;
        zoomed = Native.IsZoomed(h);
        return true;
    }

    private static List<MonitorInfo> EnumMonitors()
    {
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(0, 0, (IntPtr hMon, IntPtr hdcUnused, ref RECT rectUnused, IntPtr dataUnused) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!Native.GetMonitorInfoW(hMon, ref mi)) return true;
            uint dpi = 96;
            if (Native.GetDpiForMonitor(hMon, 0, out var dx, out _) == 0 && dx > 0) dpi = dx;
            list.Add(new MonitorInfo(list.Count,
                new RectI(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom),
                new RectI(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom),
                (int)dpi, (mi.dwFlags & 1) != 0));
            return true;
        }, 0);
        return list;
    }

    private static int MonitorIndexFor(List<MonitorInfo> monitors, RectI b)
    {
        int best = 0; long bestArea = -1;
        for (int i = 0; i < monitors.Count; i++)
        {
            long a = b.Intersect(monitors[i].Bounds).Area;
            if (a > bestArea) { bestArea = a; best = i; }
        }
        return best;
    }

    private static RectI ReadTaskbar(List<MonitorInfo> monitors)
    {
        // Primary taskbar only (secondary taskbars: future work). Auto-hidden bars sit off-screen and are dropped.
        nint h = Native.FindWindowW("Shell_TrayWnd", null);
        if (h == 0 || !Native.IsWindowVisible(h) || !Native.GetWindowRect(h, out var r)) return default;
        var rect = new RectI(r.Left, r.Top, r.Right, r.Bottom);
        return monitors.Any(m => rect.IntersectsWith(m.Bounds)) ? rect : default;
    }
}
