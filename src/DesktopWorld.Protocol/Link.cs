#nullable disable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopWorld.Protocol
{
    /// <summary>Reads '\n'-terminated ASCII lines with a hard length cap. Returns null at end of stream.</summary>
    public sealed class LineReader
    {
        private readonly Stream _s; private readonly byte[] _buf = new byte[4096]; private int _pos, _len;
        private readonly System.Text.StringBuilder _line = new System.Text.StringBuilder();
        public LineReader(Stream s) { _s = s; }

        public async Task<string> ReadLineAsync(CancellationToken ct)
        {
            while (true)
            {
                while (_pos < _len)
                {
                    byte b = _buf[_pos++];
                    if (b == (byte)'\n') { var l = _line.ToString(); _line.Clear(); return l.TrimEnd('\r'); }
                    _line.Append((char)b);
                    if (_line.Length > Wire.MaxLineBytes) throw new InvalidDataException("line exceeds " + Wire.MaxLineBytes + " bytes");
                }
                _len = await _s.ReadAsync(_buf, 0, _buf.Length, ct).ConfigureAwait(false);
                _pos = 0;
                if (_len == 0) return null;
            }
        }
    }

    /// <summary>One live connection: handshake, framing, heartbeat, stale-peer detection. Transport-agnostic.</summary>
    public sealed class Link
    {
        public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(1000);
        public static readonly TimeSpan PeerTimeout = TimeSpan.FromMilliseconds(4000);

        private readonly Stream _stream; private readonly LineReader _reader; private readonly object _wlock = new object();
        private long _seq; private long _lastRxTicks;
        private readonly string _appVersion;

        public event Action<Msg> Received;
        public event Action<string> Log;
        public string PeerRole { get; private set; }

        public Link(Stream stream, string appVersion) { _stream = stream; _reader = new LineReader(stream); _appVersion = appVersion; }

        private void Say(string s) { var h = Log; if (h != null) h(s); }

        public bool Send(Msg m)
        {
            try
            {
                lock (_wlock)
                {
                    m.Seq = ++_seq;
                    var bytes = System.Text.Encoding.ASCII.GetBytes(Codec.Encode(m) + "\n");
                    _stream.Write(bytes, 0, bytes.Length); _stream.Flush();
                }
                return true;
            }
            catch (Exception ex) { Say("send failed: " + ex.Message); return false; }
        }

        private async Task<Msg> ReadMsgAsync(CancellationToken ct)
        {
            var line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) return null;
            Msg m; string err;
            if (!Codec.TryDecode(line, out m, out err)) throw new InvalidDataException("protocol error: " + err);
            Interlocked.Exchange(ref _lastRxTicks, DateTime.UtcNow.Ticks);
            return m;
        }

        /// <summary>Runs until disconnect/cancel. Returns a human-readable end reason.
        /// Client sends HELLO first and expects WELC; server expects HELLO then sends WELC.</summary>
        public async Task<string> RunAsync(bool server, string role, CancellationToken ct)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                try
                {
                    // ---- handshake ----
                    using (var hs = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                    {
                        hs.CancelAfter(HandshakeTimeout);
                        if (!server) Send(new HelloMsg { Role = role, AppVersion = _appVersion });
                        Msg first;
                        try { first = await ReadMsgAsync(hs.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return "handshake timeout"; }
                        if (first == null) return "peer closed during handshake";
                        if (server)
                        {
                            var hello = first as HelloMsg;
                            if (hello == null) { Send(new ErrorMsg { Code = "expected_hello", Text = "first message must be HELLO" }); return "bad handshake"; }
                            PeerRole = hello.Role; Send(new WelcomeMsg { AppVersion = _appVersion });
                        }
                        else
                        {
                            var w = first as WelcomeMsg;
                            if (w == null) return "bad handshake (no WELC)";
                            PeerRole = "companion";
                        }
                    }

                    // ---- steady state ----
                    Interlocked.Exchange(ref _lastRxTicks, DateTime.UtcNow.Ticks);
                    var beat = Task.Run(async () =>
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            await Task.Delay(HeartbeatInterval, cts.Token).ConfigureAwait(false);
                            if (!Send(new PingMsg { TimestampMs = Environment.TickCount })) return "send failed";
                            var idle = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastRxTicks), DateTimeKind.Utc);
                            if (idle > PeerTimeout) return "peer timed out";
                        }
                        return "cancelled";
                    });
                    var read = Task.Run(async () =>
                    {
                        while (true)
                        {
                            var m = await ReadMsgAsync(cts.Token).ConfigureAwait(false);
                            if (m == null) return "peer closed";
                            if (m is PingMsg) { Send(new PongMsg { TimestampMs = ((PingMsg)m).TimestampMs }); continue; }
                            if (m is PongMsg || m is UnknownMsg) continue;
                            if (m is ByeMsg) return "peer said bye";
                            var h = Received; if (h != null) h(m);
                        }
                    });

                    var done = await Task.WhenAny(beat, read).ConfigureAwait(false);
                    cts.Cancel();
                    try { return await done.ConfigureAwait(false); }
                    catch (OperationCanceledException) { return "cancelled"; }
                }
                catch (OperationCanceledException) { return "cancelled"; }
                catch (InvalidDataException ex) { Send(new ErrorMsg { Code = "protocol", Text = ex.Message }); return ex.Message; }
                catch (IOException ex) { return "io: " + ex.Message; }
                catch (ObjectDisposedException) { return "closed"; }
            }
        }
    }
}
