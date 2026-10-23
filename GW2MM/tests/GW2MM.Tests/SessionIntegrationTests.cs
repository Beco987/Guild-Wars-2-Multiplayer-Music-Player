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
    private static readonly HostTimings FastTimings = new() { ClockWaitMs = 300, ClientPlayRequestDelayMs = 20, OctaveResetDelayMs = 5, PingCount = 4, PingIntervalMs = 20, SyncPreDelayMs = 1, SongTailMs = 50, PlaylistGapMs = 50, ClockProbeIntervalMs = 20 };
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

    private (HostSession Session, Machine M) StartHost(PortMapping mapping, string[]? delays = null, double offset = 0, IInputDelayMeter? meter = null,
        bool resetOctavesBetweenSongs = true, bool liveOctaveTimer = false)
    {
        var m = NewMachine(offset);
        var lib = new SongLibrary(Path.Combine(_dir, "host"));
        var host = new HostSession(m.View, lib, () => "130", m.Clock, m.Keys, meter ?? new FakeMeter(), FastTimings)
        {
            ResetOctavesBetweenSongs = () => resetOctavesBetweenSongs,
            LiveOctaveTimer = () => liveOctaveTimer,
        };
        host.CommitPortMapping(mapping, delays ?? new[] { "0", "0", "0", "0", "0" });
        host.Start(0);
        _disposables.Add(host);
        return (host, m);
    }

    private async Task<(ClientSession Session, Machine M)> StartClient(HostSession host, double offset, string name = "bob", IInputDelayMeter? meter = null, string? libDir = null,
        bool useUdpClock = true)
    {
        var m = NewMachine(offset);
        var client = new ClientSession(m.View, new SongLibrary(libDir ?? Path.Combine(_dir, "client-" + name)), () => 5, m.Clock, m.Keys, meter ?? new FakeMeter())
        {
            SyncPreDelayMs = 1,
            UseUdpClock = useUdpClock,
        };
        Assert.True(await client.ConnectAsync("127.0.0.1", name, host.Port));
        _disposables.Add(client);
        await Wait.Until(() => client.Slot is not null || m.View.Events.Any(l => l.StartsWith("Socket,")), what: "welcome");
        return (client, m);
    }

    [Fact]
    public async Task PartsPlayInSyncAcrossMachinesWithLatencyCompensation()
    {
        var mapping = PortMapping.Parse("12,23");
        var (host, h) = StartHost(mapping, new[] { "0", "40", "0", "0", "0" }, offset: 0);
        var (_, c) = await StartClient(host, offset: 1_000_000);
        await Wait.Until(() => h.View.Events.Contains("Socket2,bob"), what: "hello");
        Assert.Contains("bob joined as player 2.", h.View.Dialog);

        await host.PlayAsync(Song, 1.0);
        await Wait.Until(() => h.Keys.Presses.Count >= 5 && c.Keys.Presses.Count >= 5, timeoutMs: 8000, what: "presses");

        var hp = h.Keys.All;
        var cp = c.Keys.All.Take(5).ToList();
        // Host: track 2 -> window Player2; client (its first mapped track) -> window Player1.
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, hp.Select(p => p.Key));
        Assert.All(hp, p => Assert.Equal(2, p.Window));
        // High C alone in the middle octave plays as "8"; the next note needs the high octave. As in the original
        // (OpportunisticSwaps), the "0" swap is pressed on its own at the midpoint of the 200-tick gap, since the gap
        // is at least OctaveDelay (130).
        Assert.Equal(new[] { "8", "0", "2", "3", "4" }, cp.Select(p => p.Key));
        Assert.All(cp, p => Assert.Equal(1, p.Window));

        // One timeline: host note k at 200k ms; client presses at 100 ("8"), 200 ("0", the midpoint), 300, 500, 700 ms,
        // each 40 ms early (the client's input delay). A press can be late but never early, so the song's start is
        // estimated from the least-late host press rather than from the first one.
        var hostStart = Enumerable.Range(0, 5).Min(k => h.Clock.ToReal(hp[k].Time) - 200 * k);
        for (var k = 0; k < 5; k++)
            Assert.InRange(h.Clock.ToReal(hp[k].Time) - hostStart, 200 * k - 2, 200 * k + 15);
        var clientExpected = new[] { 100, 200, 300, 500, 700 };
        for (var k = 0; k < 5; k++)
            Assert.InRange(c.Clock.ToReal(cp[k].Time) - hostStart, clientExpected[k] - 40 - 10, clientExpected[k] - 40 + 15);

        // The client's part ends in the high octave, so it resets with one 9; the host's never left the middle one.
        await Wait.Until(() => c.Keys.All.Count == 6, what: "client reset");
        Assert.Equal(("9", 1), (c.Keys.All[5].Key, c.Keys.All[5].Window));
        await Task.Delay(200);
        Assert.Equal(5, h.Keys.All.Count);
    }

    [Fact]
    public async Task WithClocksFollowedOverUdpTheSongStartsWithoutTheProbeWaitAndInSync()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,23"));
        var (_, c) = await StartClient(host, offset: 1_000_000);
        await Wait.Until(() => host.ClockEstimateOf(2) is not null, timeoutMs: 5000, what: "UDP clock estimate");
        Assert.InRange(host.ClockEstimateOf(2)!.Value.Offset, -1_000_000 - 2, -1_000_000 + 2); // the client's clock is 1 000 000 ms ahead

        var pressed = h.Clock.NowMs;
        await host.PlayAsync(Song, 1.0);
        await Wait.Until(() => h.Keys.Presses.Count >= 5 && c.Keys.Presses.Count >= 5, timeoutMs: 8000, what: "presses");
        Assert.Contains(h.View.Events, e => e.StartsWith("Clock,Socket2,udp,", StringComparison.Ordinal));
        Assert.DoesNotContain(h.View.Events, e => e.StartsWith("Tick_Count", StringComparison.Ordinal));          // no probes
        // No probe wait: the first note comes ClockWaitMs sooner than the 4 s lead (plus the time to arrange the song).
        Assert.InRange(h.Keys.All[0].Time - pressed, Schedule.StartDelayMs - FastTimings.ClockWaitMs - 20, Schedule.StartDelayMs - FastTimings.ClockWaitMs + 200);
        Assert.InRange(SongStart(c) - SongStart(h), -5, 10);
    }

    /// <summary>
    /// When a machine's song started (real time), from the Song's notes: host track 2 at 0, 200, … ms; client track 3
    /// "8" at 100, "0" at 200, then 300, 500, 700. A press can be late but never early, so the least-late one counts.
    /// </summary>
    private static double SongStart(Machine m)
    {
        var presses = m.Keys.All.Take(5).ToList();
        var due = presses[0].Key == "8" ? new[] { 100, 200, 300, 500, 700 } : new[] { 0, 200, 400, 600, 800 };
        return Enumerable.Range(0, presses.Count).Min(k => m.Clock.ToReal(presses[k].Time) - due[k]);
    }

    [Fact]
    public async Task WithoutUdpTheHostSamplesClocksOverTcpAsBefore()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,23"));
        var (_, c) = await StartClient(host, offset: 1_000_000, useUdpClock: false);
        await Task.Delay(800);
        Assert.Null(host.ClockEstimateOf(2));
        await host.PlayAsync(Song, 1.0);
        await Wait.Until(() => h.Keys.Presses.Count >= 5 && c.Keys.Presses.Count >= 5, timeoutMs: 8000, what: "presses");
        Assert.Contains(h.View.Events, e => e.StartsWith("Clock,Socket2,tcp,", StringComparison.Ordinal));
        Assert.Contains(h.View.Events, e => e.StartsWith("Tick_Count,Socket2", StringComparison.Ordinal));
        Assert.InRange(SongStart(c) - SongStart(h), -10, 15);
    }

    [Fact]
    public async Task WithTheLiveOctaveTimerPlayersPressOctaveChangesEarly()
    {
        var (host, _) = StartHost(PortMapping.Parse("12,23"), liveOctaveTimer: true);
        var (_, c) = await StartClient(host, 0);
        await host.PlayAsync(Song, 1.0);
        await Wait.Until(() => c.Keys.Presses.Count >= 5, timeoutMs: 8000, what: "client presses");
        var cp = c.Keys.All.Take(5).ToList();   // a reset (one 9) follows when the part ends
        Assert.Equal(new[] { "8", "0", "2", "3", "4" }, cp.Select(p => p.Key));
        // The "0" goes at the first 10 ms step after the high C (the original put it at the midpoint, 100 ms later).
        Assert.InRange(cp[1].Time - cp[0].Time, 1, 25);
        Assert.InRange(cp[2].Time - cp[0].Time, 195, 215);
    }

    [Fact]
    public async Task StopHaltsEveryoneAndResetsOctaves()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,23"));
        var (_, c) = await StartClient(host, 5000);
        var slowSong = Song.Replace(", 200,", ", 2000,").Replace(", 400,", ", 4000,").Replace(", 600,", ", 6000,").Replace(", 800,", ", 8000,")
            .Replace("3, 300,", "3, 3000,");
        await host.PlayAsync(slowSong, 1.0);
        await Wait.Until(() => c.Keys.Presses.Any(p => p.Key == "0"), timeoutMs: 8000, what: "client goes up an octave");
        await host.Stop();
        await Wait.Until(() => c.View.Events.Contains("Stop"), what: "client stop");
        await Task.Delay(300);
        Assert.Equal(1, h.View.ShownMainWindow);
        // Only what each window needs: the host's part never left the middle octave; the client's is one up.
        Assert.Equal(new[] { "1" }, h.Keys.All.Select(p => p.Key));
        var clientPresses = c.Keys.All;
        Assert.Equal(new[] { "8", "0", "9" }, clientPresses.Select(p => p.Key));
        Assert.All(clientPresses, p => Assert.Equal(1, p.Window));
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
        Assert.DoesNotContain(c.View.Events, l => l.StartsWith("Note_1"));
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
    public async Task StopTypedInChatByAClientStopsTheSong()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var (client, _) = await StartClient(host, 0);
        var slow = Song.Replace(", 200,", ", 5000,");
        await host.PlayAsync(slow, 1.0, "Slow");
        await Wait.Until(() => h.Keys.Presses.Count >= 1);

        // Text that merely contains "stop" is ordinary chat.
        client.SendChat("please don't stop!");
        await Wait.Until(() => h.View.Dialog.Contains("bob > please don't stop!"), what: "chat");
        await Task.Delay(100);
        Assert.Equal(0, h.View.ShownMainWindow);
        Assert.True(host.IsPlaying);

        client.SendChat("  STOP ");
        await Wait.Until(() => h.View.ShownMainWindow == 1, what: "stop");
        Assert.False(host.IsPlaying);
        Assert.Null(h.View.NowPlaying.Last());
    }

    [Fact]
    public async Task StopTypedInChatByTheHostStopsTheSong()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var slow = Song.Replace(", 200,", ", 5000,");
        await host.PlayAsync(slow, 1.0);
        await Wait.Until(() => h.Keys.Presses.Count >= 1);
        host.SendChat("Stop");
        await Wait.Until(() => h.View.ShownMainWindow == 1, what: "stop");
        Assert.False(host.IsPlaying);
        Assert.Contains("Host > Stop", h.View.Dialog);
    }

    /// <summary>
    /// A client's chat reaches the host and every other client, labelled with the sender's name, and is only
    /// displayed: text that looks like an old protocol command does nothing on the other players.
    /// </summary>
    [Fact]
    public async Task ChatIsRelayedToEveryOtherClientWithoutTriggeringAnything()
    {
        var (host, h) = StartHost(new PortMapping());
        var (alice, a) = await StartClient(host, 0, name: "alice");
        var (_, b) = await StartClient(host, 0, name: "bob");
        var (_, c) = await StartClient(host, 0, name: "carol");
        await Wait.Until(() => h.View.Dialog.Count(l => l.Contains(" joined as player ")) == 3, what: "hellos");

        const string text = "Sync_Instrument Tick_Count Ping_Server Note_1 Play;x StartSong,1 stop now";
        alice.SendChat(text);
        await Wait.Until(() => b.View.Dialog.Contains("alice > " + text) && c.View.Dialog.Contains("alice > " + text), what: "relay");
        Assert.Contains("alice > " + text, h.View.Dialog);
        await Task.Delay(300);
        Assert.Single(a.View.Dialog, l => l == "alice > " + text); // its own line only; not echoed back

        foreach (var m in new[] { a, b, c })
        {
            Assert.Empty(m.Keys.All);
            Assert.DoesNotContain(m.View.Events, e => e is "Stop" or "Sync_Instrument" or "Tick_Count" or "Ping_Server" || e.StartsWith("Note_1"));
            Assert.Empty(m.View.Messages); // no Sync prompt
        }
        Assert.Equal(0, h.View.ShownMainWindow);
        Assert.Empty(h.Keys.All);
        Assert.DoesNotContain(h.View.Events, e => e.StartsWith("StartSong") || e.StartsWith("Play"));
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
        Assert.Contains("StartSong,Socket2,1.500000", h.View.Events);
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
    public async Task PortMappingSeesEachPlayersName()
    {
        var (host, h) = StartHost(new PortMapping());
        Assert.Equal(new PlayerInfo(1, true, true, null, null), host.PlayerOf(1));
        Assert.False(host.PlayerOf(2).Connected);
        var (alice, _) = await StartClient(host, 0, "  Alice  ");
        var (bob, _) = await StartClient(host, 0, "Bob");
        await Wait.Until(() => host.PlayerOf(2).Name == "Alice" && host.PlayerOf(3).Name == "Bob", what: "names");
        Assert.Equal("127.0.0.1", host.PlayerOf(3).Address);
        Assert.True(h.View.PlayerChanges >= 4);
        alice.Dispose();
        await Wait.Until(() => !host.PlayerOf(2).Connected, what: "slot freed");
        Assert.Null(host.PlayerOf(2).Name);
        var (carol, _) = await StartClient(host, 0, "Carol");
        await Wait.Until(() => host.PlayerOf(2).Name == "Carol", what: "slot reused with the new name");
        Assert.Equal("Bob", host.PlayerOf(3).Name);
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
        await Wait.Until(() => h.View.LastStatus == "bob is typing...");
        client.TypingChanged("");
        await Wait.Until(() => h.View.LastStatus == "Connected!");
        host.SendChat("hello there");
        await Wait.Until(() => c.View.Dialog.Contains("Host > hello there"));
        Assert.Contains("Host > hello there", h.View.Dialog);
        client.SendChat("hi host");
        await Wait.Until(() => h.View.Dialog.Contains("bob > hi host"));
        Assert.Contains("bob > hi host", c.View.Dialog);
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
    public async Task PlayersAreStillAskedToSyncWhenTheHostsMeasurementFails()
    {
        var (host, h) = StartHost(new PortMapping(), delays: new[] { "42", "0", "0", "0", "0" }, meter: new BrokenMeter());
        var (_, c) = await StartClient(host, 0, meter: new FakeMeter(50, 60, 70));
        await host.SyncAsync();
        Assert.Equal("42", h.View.InputDelays[1]);
        Assert.Contains(h.View.Problems, p => p.StartsWith("Could not measure the host's input delay: No playback device."));
        Assert.Contains("Sync: asked bob to measure their input delay.", h.View.Dialog);
        await Wait.Until(() => c.View.Messages.Any(m => m.StartsWith("Open Gw2, turn all ingame volumes to 0")), what: "client prompt");
        Assert.Contains("Sync: the host asked for your input delay.", c.View.Dialog);
        await Wait.Until(() => h.View.InputDelays.TryGetValue(2, out var v) && v == "60", what: "client input delay");
    }

    [Fact]
    public async Task SyncWithoutTheGameOpenKeepsTheOldValueAndSaysWhy()
    {
        var (host, h) = StartHost(new PortMapping(), delays: new[] { "42", "0", "0", "0", "0" }, meter: new GameClosedMeter());
        var (_, c) = await StartClient(host, 0, meter: new GameClosedMeter());
        await host.SyncAsync();
        Assert.Equal("42", h.View.InputDelays[1]);
        Assert.Contains(h.View.Problems, p => p.StartsWith("Guild Wars 2 is not open.") && p.EndsWith("input delay was not changed."));
        await Wait.Until(() => c.View.Problems.Any(p => p.StartsWith("Guild Wars 2 is not open.")), what: "client problem");
        await Wait.Until(() => h.View.InputDelays.TryGetValue(2, out var v) && v == "" && h.View.Events.Contains("Socket_Ping,Socket2,"), what: "blank report");
    }

    [Fact]
    public async Task APausedHostPlayingAloneCarriesOnWhereItWas()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,13"));   // two accounts: tracks 2 and 3 on Player2 and Player3
        var playing = host.PlayPlaylistAsync("Set", new[] { Q("A") }, 0, repeat: false);
        await Wait.Until(() => h.Keys.Presses.Count(p => p.Window == 2) >= 2, timeoutMs: 8000, what: "two notes");
        Assert.True(host.TogglePause());
        await Task.Delay(400);
        Assert.False(host.TogglePause());
        await playing.WaitAsync(TimeSpan.FromSeconds(10));
        var notes = h.Keys.All.Where(p => p.Window == 2 && !ResetKeys.Contains(p.Key)).ToList();
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, notes.Select(p => p.Key));   // the song was not cut short
        var gaps = Enumerable.Range(1, notes.Count - 1).Select(k => notes[k].Time - notes[k - 1].Time).ToList();
        Assert.All(gaps, g => Assert.True(g > 150, $"no burst after resuming (gap {g:0} ms)"));
        Assert.Contains(gaps, g => g > 450);
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

    // ------------------------------------------------------------------ playlists

    private static readonly string[] ResetKeys = { "9", "0" };

    /// <summary>The keys pressed per song: each song starts about 4 s after the one before ends, its notes are closer.</summary>
    private static List<List<string>> SongsPlayed(IEnumerable<Press> presses)
    {
        var groups = new List<List<string>>();
        double? last = null;
        foreach (var p in presses.OrderBy(p => p.Time))
        {
            if (last is null || p.Time - last > 1500) groups.Add(new List<string>());
            groups[^1].Add(p.Key);
            last = p.Time;
        }
        return groups;
    }

    private static QueuedSong Q(string name, string csv = Song) => new(name, csv, 1.0);

    [Fact]
    public async Task PlaylistPlaysEachSongInTurnAndResetsOctavesBetween()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,13"));
        await host.PlayPlaylistAsync("Set", new[] { Q("A"), Q("B") }, 0, repeat: false);

        var songs = SongsPlayed(h.Keys.All.Where(p => p.Window == 2));
        Assert.Equal(2, songs.Count);
        Assert.All(songs, s => Assert.Equal(new[] { "1", "2", "3", "4", "5" }, s));
        // Player3 (track 3) ends each song in the high octave: one 9 after each song, the only reset press.
        Assert.All(SongsPlayed(h.Keys.All.Where(p => p.Window == 3)), s => Assert.Equal(new[] { "8", "0", "2", "3", "4", "9" }, s));
        Assert.Equal(2, h.Keys.All.Count(p => p.Window == 3 && p.Key == "9"));
        Assert.Equal(new NowPlayingInfo?[] { new("A", "Set", 1, 2), new("B", "Set", 2, 2), null }, h.View.NowPlaying);
        Assert.Contains("♪ Now playing: B (2/2 in Set)", h.View.Dialog);
        Assert.Contains("♪ Playlist Set finished.", h.View.Dialog);
        Assert.False(host.IsPlaylistRunning);
    }

    [Fact]
    public async Task PlaylistCanKeepEveryonesOctavesBetweenSongs()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,23"), resetOctavesBetweenSongs: false);
        var (_, c) = await StartClient(host, 0);
        await host.PlayPlaylistAsync("Set", new[] { Q("A"), Q("B") }, 0, repeat: false);
        await Wait.Until(() => c.View.Events.Count(e => e == "Stop") == 2, what: "client stops");
        await Task.Delay(300);
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "1", "2", "3", "4", "5" }, h.Keys.All.Select(p => p.Key));
        // No reset at the end of either song: the client's part goes up an octave each time and stays there.
        Assert.Equal(new[] { "8", "0", "2", "3", "4", "8", "0", "2", "3", "4" }, c.Keys.All.Select(p => p.Key));

        // Stop still resets: the client is tracked in the high octave (two ups, kept within the top one), so one 9.
        await host.Stop();
        await Wait.Until(() => c.View.Events.Count(e => e == "Stop") == 3, what: "client stop");
        await Wait.Until(() => c.Keys.All.Count == 11, what: "client reset");
        Assert.Equal("9", c.Keys.All[^1].Key);
        await Task.Delay(100);
        Assert.Equal(10, h.Keys.All.Count);
    }

    [Fact]
    public async Task PlaylistStartsAtTheChosenSongAndClientsHearAboutIt()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var (_, c) = await StartClient(host, 0);
        await host.PlayPlaylistAsync("Set", new[] { Q("A"), Q("B"), Q("C") }, 1, repeat: false);
        Assert.Equal(2, SongsPlayed(h.Keys.All).Count);
        await Wait.Until(() => c.View.Dialog.Contains("♪ Playlist Set finished."), what: "client notice");
        Assert.Equal(new[] { "♪ Now playing: B (2/3 in Set)", "♪ Now playing: C (3/3 in Set)", "♪ Playlist Set finished." },
            c.View.Dialog.Where(l => l.StartsWith("♪")));
        Assert.Null(c.View.NowPlaying.Last());
    }

    [Fact]
    public async Task NextSkipsToTheFollowingSong()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var slow = Song.Replace(", 200,", ", 5000,");
        var playing = host.PlayPlaylistAsync("Set", new[] { Q("Slow", slow), Q("B") }, 0, repeat: false);
        await Wait.Until(() => h.Keys.Presses.Count >= 1, what: "first note");
        Assert.True(host.NextSong());
        await playing.WaitAsync(TimeSpan.FromSeconds(8));
        var songs = SongsPlayed(h.Keys.All);
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, songs[^1]);
        Assert.True(songs[0].Count < 5, "the slow song was cut short");
        Assert.False(host.NextSong());
    }

    [Fact]
    public async Task StopEndsThePlaylist()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var slow = Song.Replace(", 200,", ", 5000,");
        var playing = host.PlayPlaylistAsync("Set", new[] { Q("Slow", slow), Q("B") }, 0, repeat: false);
        await Wait.Until(() => h.Keys.Presses.Count >= 1, what: "first note");
        await host.Stop();
        await playing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(host.IsPlaylistRunning);
        Assert.DoesNotContain(h.View.Dialog, l => l.Contains("(2/2 in Set)") || l.Contains("finished"));
        Assert.Null(h.View.NowPlaying.Last());
    }

    [Fact]
    public async Task PlayingASingleSongEndsThePlaylist()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var slow = Song.Replace(", 200,", ", 5000,");
        var playing = host.PlayPlaylistAsync("Set", new[] { Q("Slow", slow), Q("B") }, 0, repeat: false);
        await Wait.Until(() => h.Keys.Presses.Count >= 1, what: "first note");
        await host.PlayAsync(Song, 1.0, "Single");
        await playing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(host.IsPlaylistRunning);
        Assert.Contains("♪ Now playing: Single", h.View.Dialog);
        Assert.DoesNotContain(h.View.Dialog, l => l.Contains("(2/2 in Set)"));
    }

    [Fact]
    public async Task RepeatStartsOverUntilStopped()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var playing = host.PlayPlaylistAsync("Loop", new[] { Q("A") }, 0, repeat: true);
        await Wait.Until(() => SongsPlayed(h.Keys.All).Count >= 2 && h.View.NowPlaying.Count(i => i is not null) >= 3, timeoutMs: 15000, what: "repeats");
        await host.Stop();
        await playing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(h.View.NowPlaying.Where(i => i is not null), i => Assert.Equal(new NowPlayingInfo("A", "Loop", 1, 1), i));
        Assert.DoesNotContain("♪ Playlist Loop finished.", h.View.Dialog);
    }

    [Fact]
    public async Task AClientsPlaylistPlaysForEveryoneFromItsOwnLibrary()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,23"));
        var clientLib = Path.Combine(_dir, "clientlib");
        Directory.CreateDirectory(Path.Combine(clientLib, "Csv"));
        File.WriteAllText(Path.Combine(clientLib, "Csv", "Scale.csv"), Song);
        File.WriteAllText(Path.Combine(clientLib, "Data.csv"), "Scale, 2, 0, test");
        var (client, c) = await StartClient(host, 0, libDir: clientLib);
        Assert.True(client.RequestPlaylist(new Playlist("Mine", new[] { "Scale", "Not here", "Scale" }), 0, shuffle: false, repeat: false));
        Assert.Contains(c.View.Problems, p => p.Contains("Not here"));

        await Wait.Until(() => h.View.Dialog.Contains("♪ Playlist Mine finished."), timeoutMs: 15000, what: "playlist end");
        Assert.Contains("bob started the playlist Mine.", h.View.Dialog);
        Assert.Equal(2, SongsPlayed(h.Keys.All).Count);
        Assert.Equal(2, c.Keys.All.Count(p => p.Key == "8")); // the client's part (track 3) opens with "8": played twice
        await Wait.Until(() => c.View.Dialog.Contains("♪ Playlist Mine finished."), what: "client notice");
        Assert.Contains("♪ Now playing: Scale (2/2 in Mine)", c.View.Dialog);
    }

    [Fact]
    public async Task AClientsSingleSongRequestIsAnnouncedByName()
    {
        var (host, h) = StartHost(PortMapping.Parse("12"));
        var clientLib = Path.Combine(_dir, "clientlib2");
        Directory.CreateDirectory(Path.Combine(clientLib, "Csv"));
        File.WriteAllText(Path.Combine(clientLib, "Csv", "Scale.csv"), Song);
        File.WriteAllText(Path.Combine(clientLib, "Data.csv"), "Scale, 2, 0, test");
        var (client, c) = await StartClient(host, 0, libDir: clientLib);
        client.RequestPlay("Scale");
        await Wait.Until(() => c.View.Dialog.Contains("♪ Now playing: Scale"), what: "notice");
        Assert.Contains("♪ Now playing: Scale", h.View.Dialog);
    }

    [Fact]
    public async Task TempoKeysWorkOnlyWhileNoPlayerIsConnected()
    {
        var (host, h) = StartHost(PortMapping.Parse("12,23"));
        await host.PlayAsync(Song, 1.0);
        await Wait.Until(() => host.IsPlaying, what: "the song to start");
        host.ChangeTempo(5);
        Assert.Contains("Tempo 105%", h.View.Dialog);

        var (_, _) = await StartClient(host, offset: 0);
        host.ChangeTempo(5);
        Assert.Contains("The tempo keys only work while no players are connected.", h.View.Dialog);
        Assert.DoesNotContain("Tempo 110%", h.View.Dialog);
    }
}
