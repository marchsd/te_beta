namespace DesktopWorld.Core;

/// <summary>Polls an <see cref="IDesktopSource"/> at a fixed interval and raises diffs. Capture errors are
/// reported and polling continues, so a transient failure never stops the watcher.</summary>
public sealed class DesktopWatcher
{
    private readonly IDesktopSource _source;
    private readonly WindowTracker _tracker = new();
    private IReadOnlyList<MonitorInfo>? _lastMonitors;

    public TimeSpan Interval { get; }
    public DesktopSnapshot? Latest { get; private set; }

    /// <summary>Raised on every poll (changes may be empty).</summary>
    public event Action<DesktopSnapshot, IReadOnlyList<WindowChange>>? Updated;
    /// <summary>Raised when monitor layout, resolution or DPI changes (and on first poll).</summary>
    public event Action<IReadOnlyList<MonitorInfo>>? MonitorsChanged;
    public event Action<Exception>? Error;

    public DesktopWatcher(IDesktopSource source, TimeSpan interval)
    {
        if (interval < TimeSpan.FromMilliseconds(10)) throw new ArgumentOutOfRangeException(nameof(interval), "Minimum interval is 10 ms.");
        _source = source;
        Interval = interval;
    }

    public void PollOnce()
    {
        var snap = _source.Capture();
        if (_lastMonitors is null || !_lastMonitors.SequenceEqual(snap.Monitors))
        {
            _lastMonitors = snap.Monitors;
            MonitorsChanged?.Invoke(snap.Monitors);
        }
        var changes = _tracker.Update(snap);
        Latest = snap;
        Updated?.Invoke(snap, changes);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try { PollOnce(); }
                catch (Exception ex) when (ex is not OperationCanceledException) { Error?.Invoke(ex); }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }
}
