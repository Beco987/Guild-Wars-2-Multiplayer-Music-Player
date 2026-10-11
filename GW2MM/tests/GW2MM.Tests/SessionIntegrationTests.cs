using GW2MM.Core.Arrangement;
using GW2MM.Core.Library;
using GW2MM.Core.Net;
using GW2MM.Core.Playback;
using GW2MM.Core.Sessions;

namespace GW2MM.Tests;

/// <summary>Host and clients talking over real loopback TCP, each "machine" on its own clock.</summary>
[Collection(TimingCollection.Name)]
public sealed class SessionIntegrationTests : IDisposable
{
    private static readonly HostTimings FastTimings = new() { ClockWaitMs = 300, ClientPlayRequestDelayMs = 20, OctaveResetDelayMs = 5, PingCount = 4, PingIntervalMs = 20, SyncPreDelayMs = 1 };
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gw2mm-it-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = new();

    public SessionIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // Track 2: a rising scale; track 3: a few notes; tempo 500000 => 1 tick == 1 ms.
    private const string Song = "0, 0, Header, 1, 3, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n1, 0, End_track\n" +
        "2, 0, Start_track\n2, 0, Note_on_c, 0, 60, 90\n2, 200, Note_on_c, 0, 62, 90\n2, 400, Note_on_c, 0, 64, 90\n2, 600, Note_on_c, 0, 65, 90\n2, 800, Note_on_c, 0, 67, 90\n2, 800, End_track\n" +
        "3, 0, Start_track\n3, 100, Note_on_c, 0, 72, 90\n3, 300, Note_on_c, 0, 74, 90\n3, 500, Note_on_c, 0, 76, 90\n3, 700, Note_on_c, 0, 77, 90\n3, 700, End_track\n0, 0, End_of_file\n";

    private sealed class Machine
    {
        public required OffsetClock Clock;
        public required RecordingKeys Keys;
        public required FakeView View;
    }

    private Machine NewMachine(double offset)
    {
        var clock = new OffsetClock(offset);
        return new Machine { Clock = clock, Keys = new RecordingKeys(clock), View = new FakeView() };
    }

    private (HostSession Session, Machine M) StartHost(PortMapping mapping, string[]? delays = null, double offset = 0, IInputDelayMeter? meter = null)
    {
        var m = NewMachine(offset);
        var lib = new SongLibrary(Path.Combine(_dir, "host"));
        var host = new HostSession(m.View, lib, () => "130", m.Clock, m.Keys, meter ?? new FakeMeter(), FastTimings);
        host.CommitPortMapping(mapping, delays ?? new[] { "0", "0", "0", "0", "0" });
        host.Start(0);
        _disposables.Add(host);
        return (host, m);
    }

    private async Task<(ClientSession Session, Machine M)> StartClient(HostSession host, double offset, string name = "bob", IInputDelayMeter? meter = null, string? libDir = null)
    {
        var m = NewMachine(offset);
        var client = new ClientSession(m.View, new SongLibrary(libDir ?? Path.Combine(_dir, "client-" + name)), () => 5, m.Clock, m.Keys, meter ?? new FakeMeter()) { SyncPreDelayMs = 1 };
        Assert.True(await client.ConnectAsync("127.0.0.1", name, host.Port));
        _disposables.Add(client);
        await Wait.Until(() => client.Slot is not null || m.View.Dialog.Any(l => l.StartsWith("Peer > Socket,")), what: "welcome");
        return (client, m);
    }

    [Fact]
    public async Task PartsPlayInSyncAcrossMachinesWithLatencyCompensation()
    {
        var mapping = PortMapping.Parse("12,23");
        var (host, h) = StartHost(mapping, new[] { "0", "40", "0", "0", "0" }, offset: 0);
        var (_, c) = await StartClient(host, offset: 1_000_000);
        await Wait.Until(() => h.View.Dialog.Contains("Peer > Socket2,bob"), what: "hello");

        await host.PlayAsync(Song, 1.0);
        await Wait.Until(() => h.Keys.Presses.Count >= 5 && c.Keys.Presses.Count >= 5, timeoutMs: 8000, what: "presses");

        var hp = h.Keys.All;
        var cp = c.Keys.All;
        // Host: track 2 -> window Player2; client (its first mapped track) -> window Player1.
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, hp.Select(p => p.Key));
        Assert.All(hp, p => Assert.Equal(2, p.Window));
        // High C alone in the middle octave plays as "8"; the next note needs the high octave, and because the gap
        // (200 ticks) is at least OctaveDelay (130) the "0" swap is pressed on its own halfway through the gap.
        Assert.Equal(new[] { "8", "0", "2", "3", "4" }, cp.Select(p => p.Key));
        Assert.All(cp, p => Assert.Equal(1, p.Window));
        Assert.InRange(c.Clock.ToReal(cp[1].Time) - c.Clock.ToReal(cp[0].Time), 100 - 10, 100 + 15);
        cp = cp.Where(p => p.Key != "0").ToList();

        // Same timeline: host note k at 200k ms, client note k at 100 + 200k ms, client 40 ms early (its input delay).
        var hostStart = h.Clock.ToReal(hp[0].Time);
        for (var k = 0; k < 4; k++)
        {
            var hostAt = h.Clock.ToReal(hp[k].Time) - hostStart;
            var clientAt = c.Clock.ToReal(cp[k].Time) - hostStart;
            Assert.InRange(hostAt, 200 * k - 5, 200 * k + 15);
            Assert.InRange(clientAt, 100 + 200 * k - 40 - 15, 100 + 200 * k - 40 + 15);
        }
    }

    [Fact]
    public async Task StopHaltsEveryoneAndResetsOctaves()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,23"));
        var (_, c) = await StartClient(host, 5000);
        var slowSong = Song.Replace(", 200,", ", 2000,").Replace(", 400,", ", 4000,").Replace(", 600,", ", 6000,").Replace(", 800,", ", 8000,");
        await host.PlayAsync(slowSong, 1.0);
        await Wait.Until(() => h.Keys.Presses.Count >= 1, what: "first note");
        await host.Stop();
        await Wait.Until(() => c.View.Dialog.Contains("Peer > Stop"), what: "client stop");
        await Task.Delay(300);
        Assert.Equal(1, h.View.ShownMainWindow);
        var hostReset = h.Keys.All.Skip(1).ToList();
        Assert.Equal(15, hostReset.Count);
        Assert.Equal(Enumerable.Repeat("9", 10).Concat(Enumerable.Repeat("0", 5)), hostReset.Select(p => p.Key));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, hostReset.Take(5).Select(p => p.Window));
        var clientPresses = c.Keys.All;
        Assert.Equal(15, clientPresses.Count(p => p.Key is "9" or "0"));
        Assert.DoesNotContain(clientPresses, p => p.Key is "2" or "3" or "4");
        Assert.False(host.IsPlaying);
    }

    [Fact]
    public async Task StopDuringPreparationNeverStartsAnyPart()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,23"));
        var (_, c) = await StartClient(host, 0);
        var play = host.PlayAsync(Song, 1.0);
        await Task.Delay(50);
        await host.Stop();
        await play;
        await Task.Delay(400);
        Assert.DoesNotContain(c.View.Dialog, l => l.StartsWith("Peer > Note_1"));
        Assert.DoesNotContain(h.Keys.All, p => p.Key is not ("9" or "0"));
        Assert.DoesNotContain(c.Keys.All, p => p.Key is not ("9" or "0"));
    }

    [Fact]
    public async Task PlayingAgainReplacesTheCurrentSong()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var slow = Song.Replace(", 200,", ", 5000,").Replace(", 400,", ", 9000,");
        await host.PlayAsync(slow, 1.0);
        await Wait.Until(() => h.Keys.Presses.Count >= 1);
        await host.PlayAsync(Song, 1.0);
        await Wait.Until(() => h.Keys.Presses.Count >= 6, timeoutMs: 8000);
        await Task.Delay(300);
        Assert.Equal(new[] { "1", "1", "2", "3", "4", "5" }, h.Keys.All.Select(p => p.Key));
    }

    [Fact]
    public async Task ChatContainingStopFromAClientStopsTheSong()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var (client, _) = await StartClient(host, 0);
        var slow = Song.Replace(", 200,", ", 5000,");
        await host.PlayAsync(slow, 1.0);
        await Wait.Until(() => h.Keys.Presses.Count >= 1);
        client.SendChat("please stop!");
        await Wait.Until(() => h.View.ShownMainWindow == 1, what: "stop");
        Assert.Contains("Peer > please stop!", h.View.Dialog);
        Assert.False(host.IsPlaying);
    }

    [Fact]
    public async Task ClientPlayRequestPlaysTheClientsSongWithItsTempo()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var clientLib = Path.Combine(_dir, "clientlib");
        Directory.CreateDirectory(Path.Combine(clientLib, "Csv"));
        File.WriteAllText(Path.Combine(clientLib, "Csv", "Scale.csv"), Song);
        File.WriteAllText(Path.Combine(clientLib, "Data.csv"), "Scale, 2, -10, test");
        var (client, _) = await StartClient(host, 0, libDir: clientLib);
        client.RequestPlay("Scale");
        await Wait.Until(() => h.Keys.Presses.Count >= 2, timeoutMs: 8000);
        Assert.Contains("Peer > StartSong,1.500000", h.View.Dialog);
        var p = h.Keys.All;
        Assert.InRange(p[1].Time - p[0].Time, 300 - 15, 300 + 15); // 200 ticks * 1.5 (a late first press shortens the gap)
        Assert.Equal(1.5, host.DefaultTempo, 6);
    }

    [Fact]
    public async Task UnansweredClockRequestSkipsThatPlayer()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,23"));
        using var silent = await PeerConnection.ConnectAsync("127.0.0.1", host.Port);
        silent.Start();
        await Wait.Until(() => host.IsSlotConnected(2));
        await host.PlayAsync(Song, 1.0);
        await Wait.Until(() => h.View.Problems.Any(), what: "problem");
        Assert.Contains("Player 2 did not answer", h.View.Problems.First());
        await Wait.Until(() => h.Keys.Presses.Count >= 1, what: "host still plays");
    }

    [Fact]
    public async Task SlotsAreReusedAfterDisconnectAndExtraClientsGetNoSlot()
    {
        var (host, h) = StartHost(new PortMapping());
        var clients = new List<ClientSession>();
        for (var i = 0; i < 4; i++) clients.Add((await StartClient(host, 0, "p" + i)).Session);
        Assert.Equal(new int?[] { 2, 3, 4, 5 }, clients.Select(c => c.Slot));
        var (extra, _) = await StartClient(host, 0, "extra");
        Assert.Null(extra.Slot);
        clients[1].Dispose();
        await Wait.Until(() => h.View.Disconnects == 1, what: "disconnect");
        Assert.Contains("The client disconnected!", h.View.Messages);
        var (again, _) = await StartClient(host, 0, "again");
        Assert.Equal(3, again.Slot);
    }

    [Fact]
    public async Task LastDisconnectReturnsToWaitingAndDisablesChat()
    {
        var (host, h) = StartHost(new PortMapping());
        var (client, _) = await StartClient(host, 0);
        await Wait.Until(() => h.View.ChatEnabled);
        client.Dispose();
        await Wait.Until(() => h.View.Disconnects == 1);
        await Wait.Until(() => !h.View.ChatEnabled);
        Assert.Equal("Waiting for a peer...", h.View.LastStatus);
    }

    [Fact]
    public async Task ChatAndTypingIndicator()
    {
        var (host, h) = StartHost(new PortMapping());
        var (client, c) = await StartClient(host, 0);
        client.TypingChanged("h");
        await Wait.Until(() => h.View.LastStatus == "Peer is typing...");
        client.TypingChanged("");
        await Wait.Until(() => h.View.LastStatus == "Connected!");
        host.SendChat("hello there");
        await Wait.Until(() => c.View.Dialog.Contains("Peer > hello there"));
        Assert.Contains("You > hello there", h.View.Dialog);
    }

    [Fact]
    public async Task PingAndSyncReportPerSlotValues()
    {
        var (host, h) = StartHost(new PortMapping(), meter: new FakeMeter(100, 111));
        var (_, c) = await StartClient(host, 0, meter: new FakeMeter(50, 60, 70));
        await host.PingAsync();
        Assert.True(h.View.ClientPings.TryGetValue(2, out var ping));
        Assert.InRange(long.Parse(ping!), 0, 50);

        await host.SyncAsync();
        Assert.Equal("106", h.View.InputDelays[1]); // Round(105.5)
        await Wait.Until(() => h.View.InputDelays.TryGetValue(2, out var v) && v == "60", what: "client input delay");
        Assert.Equal("60", host.InputDelayOf(2));
        Assert.Contains(c.View.Messages, m => m.StartsWith("Open Gw2, turn all ingame volumes to 0"));
    }

    [Fact]
    public async Task PauseHoldsTheHostWhileClientsContinue()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,22"));
        var (_, c) = await StartClient(host, 0);
        Assert.True(host.TogglePause());
        await host.PlayAsync(Song, 1.0);
        await Wait.Until(() => c.Keys.Presses.Count >= 5, timeoutMs: 8000);
        Assert.Empty(h.Keys.Presses);
        Assert.False(host.TogglePause());
        await Wait.Until(() => h.Keys.Presses.Count >= 5);
        var hp = h.Keys.All;
        Assert.True(hp[^1].Time - hp[0].Time < 50, "caught up in a burst after resuming");
    }
}
