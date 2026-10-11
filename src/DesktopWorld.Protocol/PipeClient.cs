#nullable disable
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopWorld.Protocol
{
    /// <summary>Mod-side endpoint: connects to the companion's local named pipe and reconnects with backoff.
    /// Named pipes are local to the machine and the server restricts them to the current user.</summary>
    public sealed class PipeClient
    {
        private readonly string _pipeName, _appVersion, _role;
        private Link _link;
        public event Action<Msg> Received;
        public event Action<bool> ConnectionChanged;
        public event Action<string> Log;
        public bool Connected { get { return _link != null; } }

        public PipeClient(string role, string appVersion, string pipeName = null)
        { _role = role; _appVersion = appVersion; _pipeName = pipeName ?? Wire.PipeName; }

        public bool Send(Msg m) { var l = _link; return l != null && l.Send(m); }

        public async Task RunAsync(CancellationToken ct)
        {
            int backoffMs = 250;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                    {
                        using (var cc = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            cc.CancelAfter(1000);
                            await pipe.ConnectAsync(cc.Token).ConfigureAwait(false);
                        }
                        var link = new Link(pipe, _appVersion);
                        link.Log += s => { var h = Log; if (h != null) h(s); };
                        link.Received += m => { var h = Received; if (h != null) h(m); };
                        _link = link;
                        var c = ConnectionChanged; if (c != null) c(true);
                        backoffMs = 250;
                        string why = await link.RunAsync(false, _role, ct).ConfigureAwait(false);
                        _link = null;
                        var d = ConnectionChanged; if (d != null) d(false);
                        var lg = Log; if (lg != null) lg("disconnected: " + why);
                    }
                }
                catch (OperationCanceledException) { if (ct.IsCancellationRequested) break; }
                catch (Exception ex) { var lg = Log; if (lg != null) lg("connect error: " + ex.Message); }

                try { await Task.Delay(backoffMs, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                backoffMs = Math.Min(backoffMs * 2, 2000);
            }
        }
    }
}
