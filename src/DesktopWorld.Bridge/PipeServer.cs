using System.IO.Pipes;
using DesktopWorld.Protocol;

namespace DesktopWorld.Bridge;

/// <summary>Companion-side endpoint. One client at a time; the pipe is local-only and CurrentUserOnly, so no network
/// listener is ever opened. After a disconnect it simply waits for the next client (the mod's reconnect loop).</summary>
public sealed class PipeServer
{
    private readonly string _pipeName, _appVersion;
    private Link? _link;

    public event Action<Msg>? Received;
    /// <summary>true = a client completed the handshake (send full state now); false = it went away.</summary>
    public event Action<bool>? ConnectionChanged;
    public event Action<string>? Log;
    public bool Connected => _link != null;

    public PipeServer(string appVersion, string? pipeName = null) { _appVersion = appVersion; _pipeName = pipeName ?? Wire.PipeName; }

    public bool Send(Msg m) => _link?.Send(m) ?? false;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

                var link = new Link(pipe, _appVersion);
                link.Log += s => Log?.Invoke(s);
                link.Received += m => Received?.Invoke(m);

                // Link.RunAsync performs the handshake; flip Connected only once it has completed.
                var run = link.RunAsync(true, "companion", ct);
                var announce = Task.Run(async () =>
                {
                    while (!run.IsCompleted && link.PeerRole == null) await Task.Delay(10, ct).ConfigureAwait(false);
                    if (link.PeerRole != null) { _link = link; ConnectionChanged?.Invoke(true); }
                }, ct);

                string why = await run.ConfigureAwait(false);
                try { await announce.ConfigureAwait(false); } catch (OperationCanceledException) { }
                if (_link == link) { _link = null; ConnectionChanged?.Invoke(false); }
                Log?.Invoke("client disconnected: " + why);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log?.Invoke("pipe server error: " + ex.Message);
                try { await Task.Delay(500, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
