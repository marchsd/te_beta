using System.Text.Json;
using DesktopWorld.Cli;
using DesktopWorld.Core;
using DesktopWorld.Windows;

const string Usage = """
deskworld - desktop geometry tool (Milestone 1)

  deskworld list  [--mock]                one-shot table of detected windows and their parts
  deskworld watch [--mock] [--map] [--interval MS]
                                          live window events; --map adds an ASCII debug view
  deskworld dump  [--mock]                JSON snapshot (monitors, windows, parts, world-space rects)

  deskworld sim   [--mock]                scripted headless gameplay demo (walk, mine, climb, fight) as ASCII frames

  --mock    use the built-in fake desktop (default automatically on non-Windows)
Logs: see FileLog path printed at startup of 'watch'.
""";

if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }

string cmd = args[0].ToLowerInvariant();
bool mock = args.Contains("--mock");
bool map = args.Contains("--map");
int intervalMs = 50;
for (int i = 1; i < args.Length - 1; i++)
    if (args[i] == "--interval" && int.TryParse(args[i + 1], out var v)) intervalMs = Math.Clamp(v, 10, 5000);

IDesktopSource source = CreateSource(mock);

try
{
    switch (cmd)
    {
        case "list": PrintList(source.Capture()); return 0;
        case "dump": Dump(source.Capture()); return 0;
        case "watch": return await Watch(source, intervalMs, map);
        case "sim": SimDemo.Run(source); return 0;
        default: Console.Error.WriteLine($"Unknown command '{cmd}'.\n{Usage}"); return 2;
    }
}
catch (Exception ex)
{
    FileLog.Write("cli", ex.ToString());
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static IDesktopSource CreateSource(bool forceMock)
{
    if (!forceMock && OperatingSystem.IsWindows())
    {
        DpiAwareness.Enable();
        return new Win32DesktopSource();
    }
    if (!forceMock) Console.Error.WriteLine("Not running on Windows: using the mock desktop.");
    return new MockDesktopSource();
}

static void PrintList(DesktopSnapshot s)
{
    foreach (var m in s.Monitors)
        Console.WriteLine($"Monitor {m.Index}{(m.IsPrimary ? "*" : " ")} {m.Bounds} work {m.WorkArea} dpi {m.Dpi} (scale {m.Dpi / 96.0:P0})");
    Console.WriteLine($"Virtual screen {s.VirtualScreen}   Taskbar {(s.Taskbar.IsEmpty ? "none" : s.Taskbar.ToString())}");
    Console.WriteLine();
    Console.WriteLine($"{"ID",-10}{"PID",-8}{"STATE",-11}{"Z",-4}{"MON",-4}{"BOUNDS",-26}TITLEBAR / INTERIOR");
    foreach (var w in s.Windows)
    {
        var p = WindowGeometry.Decompose(w);
        Console.WriteLine($"{w.Id:X},".TrimEnd(',').PadRight(10) + $"{w.ProcessId,-8}{w.State,-11}{w.ZOrder,-4}{w.MonitorIndex,-4}{w.Bounds,-26}{p.TitleBar} / {p.Interior}");
    }
    Console.WriteLine($"\n{s.Windows.Count} window(s).");
}

static void Dump(DesktopSnapshot s)
{
    var transform = WorldTransform.ForDesktop(s.VirtualScreen, s.Primary?.Dpi ?? 96);
    var doc = new
    {
        s.Monitors,
        virtualScreen = s.VirtualScreen,
        taskbar = s.Taskbar,
        world = new { transform.ScreenOrigin, transform.PixelsPerWorldUnit, tileSize = WorldTransform.TileSize },
        windows = s.Windows.Select(w =>
        {
            var p = WindowGeometry.Decompose(w);
            return new { w.Id, w.ProcessId, w.State, w.ZOrder, w.MonitorIndex, w.Dpi, screen = p, world = new
            {
                outer = transform.ToWorld(p.Outer), titleBar = transform.ToWorld(p.TitleBar),
                interior = transform.ToWorld(p.Interior)
            } };
        })
    };
    Console.WriteLine(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
}

static async Task<int> Watch(IDesktopSource source, int intervalMs, bool map)
{
    var watcher = new DesktopWatcher(source, TimeSpan.FromMilliseconds(intervalMs));
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    long lastMap = 0;
    bool interactive = !Console.IsOutputRedirected;
    FileLog.Write("watch", $"started interval={intervalMs}ms source={source.GetType().Name}");
    Console.WriteLine($"Watching every {intervalMs} ms. Ctrl+C to stop. Log dir: {FileLog.Directory}");

    watcher.MonitorsChanged += ms => Console.WriteLine($"[monitors] {string.Join("; ", ms.Select(m => $"#{m.Index} {m.Bounds} dpi{m.Dpi}"))}");
    watcher.Error += ex => { FileLog.Write("watcher", ex.ToString()); Console.Error.WriteLine($"[error] {ex.Message}"); };
    watcher.Updated += (snap, changes) =>
    {
        foreach (var c in changes)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} {c.Kind,-13} id={c.Id:X} " + c.Kind switch
            {
                WindowChangeKind.Added => $"{c.Current!.Bounds} {c.Current.State}",
                WindowChangeKind.Removed => $"was {c.Previous!.Bounds}",
                WindowChangeKind.ZOrderChanged => $"z {c.Previous!.ZOrder}->{c.Current!.ZOrder}",
                _ => $"{c.Previous!.Bounds} -> {c.Current!.Bounds} {c.Current.State}"
            };
            if (!(map && interactive)) Console.WriteLine(line);
            FileLog.Write("window", line);
        }
        if (map && interactive && Environment.TickCount64 - lastMap > 250)
        {
            lastMap = Environment.TickCount64;
            int cols = Math.Clamp(Console.WindowWidth - 1, 40, 160), rows = Math.Clamp(Console.WindowHeight - 4, 10, 45);
            Console.SetCursorPosition(0, 0);
            Console.Write(AsciiMap.Render(snap, cols, rows));
            Console.WriteLine($"{snap.Windows.Count} windows | virtual {snap.VirtualScreen} | Ctrl+C to stop".PadRight(cols));
        }
        else if (map && !interactive && changes.Count > 0)
            Console.WriteLine(AsciiMap.Render(snap, 100, 28));
    };

    if (map && interactive) Console.Clear();
    await watcher.RunAsync(cts.Token);
    FileLog.Write("watch", "stopped");
    return 0;
}
