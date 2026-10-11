using System.IO.Pipes;
using DesktopWorld.Bridge;
using DesktopWorld.Protocol;

namespace DesktopWorld.Tests;

public class CodecTests
{
    static T RoundTrip<T>(T m) where T : Msg
    {
        m.Seq = 42;
        var line = Codec.Encode(m);
        Assert.True(Codec.TryDecode(line, out var back, out var err), err);
        Assert.Equal(42L, back.Seq);
        return (T)back;
    }

    [Fact] public void AllMessageTypesRoundTrip()
    {
        Assert.Equal("mod", RoundTrip(new HelloMsg { Role = "mod", AppVersion = "0.1.0" }).Role);
        Assert.Equal("0.1.0", RoundTrip(new WelcomeMsg { AppVersion = "0.1.0" }).AppVersion);
        Assert.Equal(123456789012L, RoundTrip(new PingMsg { TimestampMs = 123456789012L }).TimestampMs);
        Assert.Equal(5L, RoundTrip(new PongMsg { TimestampMs = 5 }).TimestampMs);
        var mode = RoundTrip(new ModeMsg { Active = true, Paused = false }); Assert.True(mode.Active && !mode.Paused);
        var mine = RoundTrip(new MineMsg { X = -1920, Y = 77 }); Assert.Equal(-1920, mine.X);
        var sp = RoundTrip(new SpawnMsg { Kind = "BlueSlime", X = -1, Y = -1 }); Assert.Equal("BlueSlime", sp.Kind);
        var ps = RoundTrip(new PlayerStateMsg { Hp = 80, MaxHp = 100, X = 10, Y = 20, InInterior = true }); Assert.True(ps.InInterior);
        Assert.Equal("bad_thing", RoundTrip(new ErrorMsg { Code = "bad_thing", Text = "oh no | pipes\nand newlines" }).Code);
        RoundTrip(new ByeMsg());
    }

    [Fact] public void GeometryRoundTripIncludingNegativeCoordsAndFractionScale()
    {
        var g = new GeometryMsg { WorldVersion = 9, PixelsPerWorldUnit = 1.5, OriginX = -1920, OriginY = 0 };
        g.Solids.Add(new Rect(-1900, 10, -1000, 42)); g.Solids.Add(new Rect(0, 1040, 1920, 1080));
        g.Interiors.Add(new Rect(-1894, 42, -1006, 500));
        var b = RoundTrip(g);
        Assert.Equal(9, b.WorldVersion); Assert.Near(1.5, b.PixelsPerWorldUnit);
        Assert.Equal(2, b.Solids.Count); Assert.Equal(-1900, b.Solids[0].Left); Assert.Equal(1, b.Interiors.Count);
        Assert.Equal(0, RoundTrip(new GeometryMsg { PixelsPerWorldUnit = 1 }).Solids.Count);
    }

    [Fact] public void RejectsMalformedInput()
    {
        string[] bad = { "", "hello", "DW2|PING|1|5", "DW1|PING", "DW1|PING|x|5", "DW1|PING|-1|5", "DW1|PING|1|abc", "DW1|PING|1|5|extra",
            "DW1|MODE|1|2|0", "DW1|MINE|1|99999999999|1", "DW1|GEOM|1|1|0|0|0||", "DW1|GEOM|1|1|NaN|0|0||", "DW1|GEOM|1|1|1|0|0|1,2,3|",
            "DW1|HELLO|1|mo|d|x", "DW1|HELLO|1|bad role|v", "DW1|PING|1|5\u0001", "DW1|SPAWN|1|sl\u00e9me|1|1" };
        foreach (var l in bad) Assert.True(!Codec.TryDecode(l, out var m, out var e) && m == null && e != null, "should reject: " + l);
        Assert.True(!Codec.TryDecode(new string('a', Wire.MaxLineBytes + 1), out _, out _));
        Assert.True(!Codec.TryDecode(null, out _, out _));
    }

    [Fact] public void UnknownTypeIsForwardCompatible()
    {
        Assert.True(Codec.TryDecode("DW1|NEWTHING|7|whatever|1", out var m, out _));
        Assert.True(m is UnknownMsg);
    }

    [Fact] public void TooManyRectsRejected()
    {
        var items = string.Join(";", Enumerable.Repeat("0,0,1,1", Wire.MaxListItems + 1));
        Assert.True(!Codec.TryDecode($"DW1|GEOM|1|1|1|0|0|{items}|", out _, out _));
    }
}

public class BridgeTests
{
    static string UniqueName() => "dw-test-" + Guid.NewGuid().ToString("N");

    static async Task<bool> WaitFor(Func<bool> cond, int ms = 5000) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { if (cond()) return true; await Task.Delay(20); } return cond(); }

    [Fact] public void HandshakeAndMessagesFlowBothWays() => Task.Run(async () =>
    {
        var name = UniqueName(); using var cts = new CancellationTokenSource();
        var server = new PipeServer("test", name); var client = new PipeClient("mod", "test", name);
        var serverGot = new List<Msg>(); var clientGot = new List<Msg>(); bool serverUp = false;
        server.Received += m => { lock (serverGot) serverGot.Add(m); };
        client.Received += m => { lock (clientGot) clientGot.Add(m); };
        server.ConnectionChanged += up => serverUp = up;
        var st = server.RunAsync(cts.Token); var ct = client.RunAsync(cts.Token);

        Assert.True(await WaitFor(() => serverUp && client.Connected), "connected");
        var g = new GeometryMsg { WorldVersion = 1, PixelsPerWorldUnit = 1 }; g.Solids.Add(new Rect(0, 0, 10, 10));
        Assert.True(server.Send(g)); Assert.True(client.Send(new MineMsg { X = 5, Y = 6 }));
        Assert.True(await WaitFor(() => { lock (clientGot) return clientGot.OfType<GeometryMsg>().Any(); }), "client got geometry");
        Assert.True(await WaitFor(() => { lock (serverGot) return serverGot.OfType<MineMsg>().Any(); }), "server got mine");
        cts.Cancel(); try { await Task.WhenAll(st, ct); } catch { }
    }).GetAwaiter().GetResult();

    [Fact] public void ClientReconnectsAfterServerRestart() => Task.Run(async () =>
    {
        var name = UniqueName(); using var all = new CancellationTokenSource();
        var client = new PipeClient("mod", "test", name); var ct = client.RunAsync(all.Token);
        int ups = 0; client.ConnectionChanged += up => { if (up) Interlocked.Increment(ref ups); };

        using (var s1 = new CancellationTokenSource())
        {
            var server = new PipeServer("test", name); var st = server.RunAsync(s1.Token);
            Assert.True(await WaitFor(() => ups == 1), "first connect");
            s1.Cancel(); try { await st; } catch { }
        }
        Assert.True(await WaitFor(() => !client.Connected), "client noticed disconnect");

        var server2 = new PipeServer("test", name); var st2 = server2.RunAsync(all.Token);
        Assert.True(await WaitFor(() => ups == 2, 8000), "client reconnected");
        all.Cancel(); try { await Task.WhenAll(ct, st2); } catch { }
    }).GetAwaiter().GetResult();

    [Fact] public void GarbageClientIsDroppedAndServerStillAcceptsNext() => Task.Run(async () =>
    {
        var name = UniqueName(); using var cts = new CancellationTokenSource();
        var server = new PipeServer("test", name); var logs = new List<string>(); server.Log += s => { lock (logs) logs.Add(s); };
        var st = server.RunAsync(cts.Token);

        using (var raw = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await raw.ConnectAsync(2000);
            var junk = System.Text.Encoding.ASCII.GetBytes("this is not the protocol\n"); await raw.WriteAsync(junk, 0, junk.Length); await raw.FlushAsync();
            Assert.True(await WaitFor(() => { lock (logs) return logs.Any(l => l.Contains("disconnected")); }), "server dropped garbage client");
        }

        var client = new PipeClient("mod", "test", name); var ct = client.RunAsync(cts.Token);
        Assert.True(await WaitFor(() => client.Connected), "legit client accepted afterwards");
        cts.Cancel(); try { await Task.WhenAll(st, ct); } catch { }
    }).GetAwaiter().GetResult();

    [Fact] public void SilentPeerIsTimedOut() => Task.Run(async () =>
    {
        var name = UniqueName(); using var cts = new CancellationTokenSource();
        var server = new PipeServer("test", name); bool up = false, down = false;
        server.ConnectionChanged += u => { if (u) up = true; else down = true; };
        var st = server.RunAsync(cts.Token);
        using var raw = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await raw.ConnectAsync(2000);
        var hello = System.Text.Encoding.ASCII.GetBytes("DW1|HELLO|1|mod|test\n"); await raw.WriteAsync(hello, 0, hello.Length); await raw.FlushAsync();
        Assert.True(await WaitFor(() => up), "handshake done");
        // never answer pings -> server must give up after PeerTimeout
        Assert.True(await WaitFor(() => down, 9000), "silent peer timed out");
        cts.Cancel(); try { await st; } catch { }
    }).GetAwaiter().GetResult();

    [Fact] public void FirstMessageMustBeHello() => Task.Run(async () =>
    {
        var name = UniqueName(); using var cts = new CancellationTokenSource();
        var server = new PipeServer("test", name); bool up = false; server.ConnectionChanged += u => up |= u;
        var st = server.RunAsync(cts.Token);
        using var raw = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await raw.ConnectAsync(2000);
        var b = System.Text.Encoding.ASCII.GetBytes("DW1|PING|1|5\n"); await raw.WriteAsync(b, 0, b.Length); await raw.FlushAsync();
        var buf = new byte[256]; int n = await raw.ReadAsync(buf, 0, buf.Length);
        Assert.True(System.Text.Encoding.ASCII.GetString(buf, 0, n).Contains("ERR"), "server replied with ERR");
        Assert.True(!up, "never announced as connected");
        cts.Cancel(); try { await st; } catch { }
    }).GetAwaiter().GetResult();
}
