using System.Globalization;
using GW2MM.Core.Ahk;
using GW2MM.Core.Arrangement;
using GW2MM.Core.Library;
using GW2MM.Core.Net;
using GW2MM.Core.Playback;
using GW2MM.Core.Text;

namespace GW2MM.Core.Sessions;

/// <summary>
/// The host ("server") side: accepts clients into player slots 2..5, owns the port mapping and input delays, and
/// runs songs: arrange -> ask every client for its clock -> wait 2 s -> send each client its part in its own clock ->
/// play the host's part. All shared state is guarded by one lock so user commands, network events and playback
/// transitions cannot interleave inconsistently.
/// </summary>
public sealed class HostSession : IDisposable
{

    private readonly object _gate = new();
    private readonly IHostView _view;
    private readonly IClock _clock;
    private readonly IKeyOutput _keys;
    private readonly IInputDelayMeter _meter;
    private readonly PeerConnection?[] _slots = new PeerConnection?[PortMapping.Slots + 1];
    private readonly List<PeerConnection> _connections = new();
    private readonly string[] _inputDelays = Enumerable.Repeat("0", PortMapping.Slots + 1).ToArray();
    private readonly string?[] _names = new string?[PortMapping.Slots + 1];
    /// <summary>Every connection's user name (also clients without a player slot), for chat.</summary>
    private readonly Dictionary<PeerConnection, string> _connNames = new();
    private PortMapping _mapping = new();
    private System.Net.Sockets.TcpListener? _listener;
    private SongRun? _current;
    private bool _paused;
    private int _nextId;
    private int _pingBusy, _syncBusy;
    private readonly Func<int> _offsetMs;

    /// <param name="offsetMs">Settings → Offset: while no player is connected, spread the host's windows' starts over
    /// this many ms (see <see cref="Schedule.Stagger"/>).</param>
    public HostSession(IHostView view, SongLibrary library, Func<string> octaveDelay, IClock clock, IKeyOutput keys, IInputDelayMeter meter,
        HostTimings? timings = null, Func<int>? offsetMs = null)
    {
        _offsetMs = offsetMs ?? (() => 0);
        Timings = timings ?? new HostTimings();
        _view = view;
        Library = library;
        OctaveDelay = octaveDelay;
        _clock = clock;
        _keys = keys;
        _meter = meter;
    }

    public SongLibrary Library { get; }
    public HostTimings Timings { get; }
    public Func<string> OctaveDelay { get; }

    /// <summary>
    /// The tempo factor of the last song started (the song's Data.csv tempo).
    /// </summary>
    public double DefaultTempo { get; private set; } = 1;

    /// <summary>
    /// F10 / F11 (not in the original, whose tempo keys had no audible effect): plays the rest of the current song
    /// <paramref name="percent"/> % of its tempo faster (or slower). Only while no player is connected: players on
    /// other computers follow the host's timing. Each new song starts at 100%.
    /// </summary>
    public void ChangeTempo(int percent)
    {
        lock (_gate)
        {
            if (_connections.Count > 0)
            {
                _view.AppendDialog("The tempo keys only work while no players are connected.");
                return;
            }
            if (_current?.Loop is not { Completion.IsCompleted: false } loop) return;
            var speed = Math.Clamp(Math.Round(loop.Speed * 100) + percent, 25, 400) / 100.0;
            loop.SetSpeed(speed);
            _current.EndMs = loop.LastDueMs ?? _current.EndMs;
            _view.AppendDialog($"Tempo {speed * 100:0}%");
        }
    }

    public int Port { get; private set; }
    public bool IsPaused { get { lock (_gate) return _paused; } }
    public bool IsPlaying { get { lock (_gate) return _current is not null; } }
    public bool AnyClientConnected { get { lock (_gate) return _connections.Count > 0; } }
    public PortMapping Mapping { get { lock (_gate) return _mapping.Clone(); } }
    public string InputDelayOf(int slot) { lock (_gate) return _inputDelays[slot]; }
    public bool IsSlotConnected(int slot) { lock (_gate) return slot == 1 || _slots[slot] is not null; }

    /// <summary>Who is in a player slot (for the Port Mapping window). Slot 1 is always this computer.</summary>
    public PlayerInfo PlayerOf(int slot)
    {
        lock (_gate)
        {
            if (slot == 1) return new PlayerInfo(1, true, true, null, null);
            var conn = _slots[slot];
            return conn is null ? new PlayerInfo(slot, false, false, null, null) : new PlayerInfo(slot, true, false, _names[slot], conn.RemoteAddress);
        }
    }

    /// <summary>The port mapping window's Save (and the commit that happens when a client disconnects).</summary>
    public void CommitPortMapping(PortMapping mapping, IReadOnlyList<string> inputDelays)
    {
        lock (_gate)
        {
            _mapping = mapping.Clone();
            for (var slot = 1; slot <= PortMapping.Slots; slot++) _inputDelays[slot] = inputDelays[slot - 1];
        }
    }

    // ------------------------------------------------------------------ connections

    public void Start(int port = PeerConnection.DefaultPort)
    {
        System.Net.Sockets.TcpListener listener;
        try
        {
            // IPv4 and IPv6 on one socket where the system supports it; plain IPv4 otherwise.
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.IPv6Any, port);
            listener.Server.DualMode = true;
            listener.Start();
        }
        catch (System.Net.Sockets.SocketException)
        {
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
            listener.Start();
        }
        _listener = listener;
        Port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        _view.SetStatus("Waiting for a peer...");
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        var listener = _listener!;
        while (true)
        {
            System.Net.Sockets.TcpClient client;
            try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch (Exception) { return; }
            var conn = new PeerConnection(client);
            int? slot = null;
            lock (_gate)
            {
                for (var s = 2; s <= PortMapping.Slots; s++)
                    if (_slots[s] is null) { _slots[s] = conn; slot = s; break; }
                _connections.Add(conn);
            }
            conn.Received += OnReceived;
            conn.Closed += OnClosed;
            conn.Start();
            conn.Send(new Welcome(slot));
            if (slot is not null) _view.OnPlayersChanged();
            _view.SetChatEnabled(true);
            _view.SetStatus($"Connected to {conn.RemoteAddress}!");
        }
    }

    private int SlotOf(PeerConnection conn)
    {
        lock (_gate)
            for (var s = 2; s <= PortMapping.Slots; s++)
                if (ReferenceEquals(_slots[s], conn)) return s;
        return 0;
    }

    private void OnClosed(PeerConnection conn)
    {
        bool none;
        lock (_gate)
        {
            for (var s = 2; s <= PortMapping.Slots; s++)
                if (ReferenceEquals(_slots[s], conn)) { _slots[s] = null; _names[s] = null; }
            _connections.Remove(conn);
            _connNames.Remove(conn);
            none = _connections.Count == 0;
        }
        _view.ShowMessage("The client disconnected!");
        if (none)
        {
            _view.SetStatus("Waiting for a peer...");
            _view.SetChatEnabled(false);
        }
        _view.OnClientDisconnected();
        _view.OnPlayersChanged();
    }

    private void OnReceived(PeerConnection conn, Message message)
    {
        var slot = SlotOf(conn);
        var socket = "Socket" + (slot == 0 ? "" : slot.ToString(CultureInfo.InvariantCulture));
        switch (message)
        {
            case ClockReply r:
                lock (_gate) _current?.OnClockReply(slot, r.Id, r.Tick, _clock.NowMs);
                _view.LogEvent($"Tick_Count,{socket},{r.Tick.ToString("0", CultureInfo.InvariantCulture)}");
                break;
            case Pong p:
                PingTracker.OnPong(slot, p.Id, _clock.NowMs);
                _view.LogEvent($"Return_Ping,{socket}");
                break;
            case Chat c:
                OnChat(conn, c.Text);
                break;
            case Typing t:
                _view.SetStatus(t.Active ? $"{NameOf(conn)} is typing..." : "Connected!");
                break;
            case Hello h:
                _view.LogEvent($"{socket},{h.Name}");
                var name = h.Name.Trim();
                if (name.Length > 40) name = name[..40];
                lock (_gate)
                {
                    if (name.Length > 0) _connNames[conn] = name;
                    if (slot != 0) _names[slot] = name;
                }
                _view.AppendDialog(slot != 0 ? $"{NameOf(conn)} joined as player {slot}." : $"{NameOf(conn)} joined (all player slots are taken).");
                if (slot != 0) _view.OnPlayersChanged();
                break;
            case InputDelayReport d:
                var value = d.Ms?.ToString(CultureInfo.InvariantCulture) ?? "";
                _view.LogEvent($"Socket_Ping,{socket},{value}");
                if (slot != 0)
                {
                    lock (_gate) _inputDelays[slot] = value;
                    _view.SetInputDelay(slot, value);
                }
                break;
            case PlayRequest pr:
                _view.LogEvent($"Play,{socket} ({LegacyText.Lines(pr.Csv).Count} lines)");
                _view.LogEvent($"StartSong,{socket},{pr.Tempo}");
                _ = PlayFromClientAsync(pr);
                break;
            case PlaylistRequest pl:
                _view.LogEvent($"Playlist,{socket},{pl.Name} ({pl.Songs.Length} songs)");
                _ = PlayPlaylistFromClientAsync(conn, pl);
                break;
        }
    }

    // ------------------------------------------------------------------ chat

    /// <summary>The host's chat box: shown here and sent to every client. "stop" also stops the song.</summary>
    public void SendChat(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _view.AppendDialog(ChatRules.Line(ChatRules.HostName, text));
        Broadcast(new Chat(text, ChatRules.HostName), except: null);
        if (ChatRules.IsStopCommand(text)) _ = Stop();
    }

    /// <summary>
    /// A client's chat message: shown here and passed on to every other client, labelled with the sender's name.
    /// It never triggers anything on the other clients; "stop" stops the song (a client has no Stop hotkey).
    /// </summary>
    private void OnChat(PeerConnection from, string text)
    {
        var name = NameOf(from);
        _view.AppendDialog(ChatRules.Line(name, text));
        Broadcast(new Chat(text, name), except: from);
        if (ChatRules.IsStopCommand(text)) _ = Stop();
    }

    private void Broadcast(Message message, PeerConnection? except)
    {
        List<PeerConnection> all;
        lock (_gate) all = _connections.Where(c => !ReferenceEquals(c, except)).ToList();
        foreach (var c in all) c.Send(message);
    }

    /// <summary>A client's user name, or "Player n" when it sent none.</summary>
    private string NameOf(PeerConnection conn)
    {
        lock (_gate)
        {
            if (_connNames.TryGetValue(conn, out var name)) return name;
            for (var s = 2; s <= PortMapping.Slots; s++)
                if (ReferenceEquals(_slots[s], conn)) return "Player " + s.ToString(CultureInfo.InvariantCulture);
        }
        return "Player";
    }

    // ------------------------------------------------------------------ playback

    /// <summary>F1 / the Play button: plays the selected song from this library.</summary>
    public Task PlayLocalAsync(string? songName)
    {
        var name = songName ?? "";
        var csv = Library.ReadSongCsv(name) ?? "";
        return PlayAsync(csv, Library.TempoFactor(name), name.Length > 0 ? name : null, Library.IsSingleInstrument(name));
    }

    private async Task PlayFromClientAsync(PlayRequest request)
    {
        await Task.Delay(Timings.ClientPlayRequestDelayMs).ConfigureAwait(false);
        await PlayAsync(request.Csv, AhkValue.Number(request.Tempo), request.Name, request.SingleInstrument).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a song for everyone. A song that is already preparing or playing is abandoned first (without the octave
    /// reset a Stop does), so two songs can never play at once. A running playlist stops.
    /// </summary>
    public async Task PlayAsync(string csvText, double? tempoFactor, string? songName = null, bool singleInstrument = false)
    {
        CancelPlaylist();
        if (songName is not null) Announce(new NowPlayingInfo(songName, null, 0, 0));
        await StartSongAsync(csvText, tempoFactor, singleInstrument).ConfigureAwait(false);
    }

    /// <summary>
    /// Arranges and starts a song (abandoning any current one). Returns the started run, whose <see cref="SongRun.EndMs"/>
    /// is when the last note of any part is due on this computer's clock; null when the song could not be prepared or
    /// was replaced or stopped before it started.
    /// </summary>
    private async Task<SongRun?> StartSongAsync(string csvText, double? tempoFactor, bool singleInstrument = false)
    {
        var run = new SongRun();
        lock (_gate)
        {
            _current?.Cancel();
            _current = run;
            if (tempoFactor is { } t) DefaultTempo = t;
        }

        SongPlan plan;
        PortMapping mapping;
        lock (_gate) mapping = _mapping.Clone();
        try
        {
            plan = SongArranger.Arrange(CsvSong.Parse(LegacyText.Lines(csvText)), mapping, OctaveDelay(), tempoFactor, singleInstrument: singleInstrument);
        }
        catch (Exception ex)
        {
            _view.ReportProblem("Could not prepare the song: " + ex.Message);
            lock (_gate) if (_current == run) _current = null;
            return null;
        }

        // Sample every machine's clock at (about) the same moment T1.
        List<(int Slot, PeerConnection Conn)> clients;
        lock (_gate)
        {
            clients = Enumerable.Range(2, PortMapping.Slots - 1).Where(s => _slots[s] is not null).Select(s => (s, _slots[s]!)).ToList();
            run.T1 = _clock.NowMs;
            SendClockProbes(run, clients);
            run.Ticks[1] = _clock.NowMs;
        }

        // More probes a little apart: a single one could hit a lag spike and shift that player for the whole song.
        // The reply with the shortest round trip is kept (ClockSampler).
        try
        {
            for (var p = 1; p < Timings.ClockProbes; p++)
            {
                await Task.Delay(Timings.ClockProbeIntervalMs, run.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_current != run) return null;
                    SendClockProbes(run, clients.Where(c => ReferenceEquals(_slots[c.Slot], c.Conn)).ToList());
                }
            }
            var remaining = run.T1 + Timings.ClockWaitMs - _clock.NowMs;
            if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), run.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }

        lock (_gate)
        {
            if (run.Token.IsCancellationRequested || _current != run) return null;
            var hostDelay = DelayNumber(_inputDelays[1]);
            var hostTick = run.Ticks[1]!.Value;
            var end = _clock.NowMs;
            for (var slot = 2; slot <= PortMapping.Slots; slot++)
            {
                var part = plan[slot];
                if (!part.IsActive) continue;
                var conn = _slots[slot];
                if (conn is null) continue;
                if (run.Ticks[slot] is not { } tick)
                {
                    _view.ReportProblem($"Player {slot} did not answer the clock request in time, so its part was not sent.");
                    continue;
                }
                var times = Schedule.AbsoluteTimes(part, tick, DelayNumber(_inputDelays[slot]) - hostDelay);
                conn.Send(new SongPart(part.Notes.ToArray(), times.Select(x => (double?)x).ToArray()));
                end = Math.Max(end, LastTime(Schedule.AbsoluteTimes(part, hostTick, 0)));
            }

            var host = plan[1];
            var hostTimes = host.IsActive ? Schedule.AbsoluteTimes(host, hostTick, 0) : new List<double>();
            var hostEvents = Schedule.HostEvents(host.Notes, hostTimes);
            // Playing alone (no player connected): Offset staggers the host's windows. Never with players, who must
            // follow the host's timing.
            if (_connections.Count == 0) hostEvents = Schedule.Stagger(hostEvents, _offsetMs(), _clock.NowMs);
            end = Math.Max(end, LastTime(hostTimes));
            if (hostEvents.Count > 0 && hostEvents[^1].DueMs is { } lastDue) end = Math.Max(end, lastDue);
            run.EndMs = end;
            var loop = new PlaybackLoop(hostEvents, _clock, _keys, _paused);
            loop.KeyError += ex => _view.ReportProblem("Key press failed: " + ex.Message);
            run.Loop = loop;
            loop.Start();
            _ = loop.Completion.ContinueWith(_ => { lock (_gate) if (_current == run) _current = null; }, TaskScheduler.Default);
            return run;
        }

        static double LastTime(List<double> times) => times.Count == 0 ? double.NegativeInfinity : times[^1];
    }

    // ------------------------------------------------------------------ playlists

    private PlaylistRunner? _runner;
    private SongRun? _playlistRun;

    private PlaylistRunner Runner => _runner ??= new PlaylistRunner(_clock,
        start: async song =>
        {
            var run = await StartSongAsync(song.Csv, song.Tempo, song.SingleInstrument).ConfigureAwait(false);
            lock (_gate) _playlistRun = run;
            return run?.EndMs;
        },

        end: () =>
        {
            SongRun? run;
            lock (_gate) run = _playlistRun;
            return run is null ? Task.CompletedTask : EndSongAsync(run);
        },
        announce: Announce,
        ended: (name, count, finished) =>
        {
            if (finished) _view.AppendDialog($"♪ Playlist {name} finished.");
            _view.OnNowPlaying(null);
            Broadcast(new NowPlaying(null, name, 0, count, finished), except: null);
        },
        report: _view.ReportProblem)
    {
        TailMs = Timings.SongTailMs,
        CurrentEnd = () => { lock (_gate) return _playlistRun?.EndMs; },
        GapMs = Timings.PlaylistGapMs,
    };

    /// <summary>True while a playlist is running (between its first song and its end, Stop, or another song).</summary>
    public bool IsPlaylistRunning => _runner?.IsRunning ?? false;

    /// <summary>Play button on the Playlists tab: plays one of this library's playlists for everyone.</summary>
    public Task PlayPlaylistLocalAsync(Playlist playlist, int startIndex, bool shuffle, bool repeat)
    {
        var (songs, first) = QueuedSongs.FromLibrary(Library, playlist, startIndex, shuffle, _view.ReportProblem);
        return PlayPlaylistAsync(playlist.Name, songs, first, repeat);
    }

    private async Task PlayPlaylistFromClientAsync(PeerConnection from, PlaylistRequest request)
    {
        await Task.Delay(Timings.ClientPlayRequestDelayMs).ConfigureAwait(false);
        var songs = request.Songs.Select((s, i) => new QueuedSong(string.IsNullOrWhiteSpace(s.Name) ? $"Song {i + 1}" : s.Name!, s.Csv, AhkValue.Number(s.Tempo), s.SingleInstrument)).ToList();
        _view.AppendDialog($"{NameOf(from)} started the playlist {request.Name}.");
        await PlayPlaylistAsync(request.Name, songs, request.StartIndex, request.Repeat).ConfigureAwait(false);
    }

    /// <summary>
    /// Plays the songs one after another for everyone (see <see cref="PlaylistRunner"/>): after each song (when the
    /// last note of every part is due, plus <see cref="HostTimings.SongTailMs"/>) everyone's octaves are reset and the
    /// next song starts after <see cref="HostTimings.PlaylistGapMs"/>. Stop, playing another song or another playlist
    /// ends it; <see cref="NextSong"/> skips ahead.
    /// </summary>
    public Task PlayPlaylistAsync(string name, IReadOnlyList<QueuedSong> songs, int startIndex, bool repeat) =>
        Runner.RunAsync(name, songs, startIndex, repeat);

    /// <summary>Next (hotkey / button): ends the current playlist song now and moves on. False when no playlist is playing.</summary>
    public bool NextSong() => _runner?.Next() ?? false;

    /// <summary>A playlist song is over (or skipped): stop whatever is left of it everywhere and reset everyone's octaves.</summary>
    private Task EndSongAsync(SongRun run)
    {
        List<PeerConnection> clients;
        lock (_gate)
        {
            if (_current == run)
            {
                run.Cancel();
                _current = null;
            }
            clients = _slots.Where(c => c is not null).Select(c => c!).ToList();
        }
        foreach (var c in clients) c.Send(new StopPlayback());
        return ResetOctavesAsync();
    }

    private void CancelPlaylist()
    {
        if (_runner?.Cancel() != true) return;
        _view.OnNowPlaying(null);
        Broadcast(new NowPlaying(null, null, 0, 0), except: null);
    }

    /// <summary>Tells everyone (this window and every client) what is now playing.</summary>
    private void Announce(NowPlayingInfo info)
    {
        _view.AppendDialog(info.Notice);
        _view.OnNowPlaying(info);
        Broadcast(new NowPlaying(info.Song, info.Playlist, info.Index, info.Count), except: null);
    }

    /// <summary>Sends one clock probe ("Tick_Count") to each client. Call under the lock.</summary>
    private void SendClockProbes(SongRun run, List<(int Slot, PeerConnection Conn)> clients)
    {
        foreach (var (slot, conn) in clients)
        {
            var id = ++_nextId;
            run.ExpectClock(slot, id, _clock.NowMs);
            conn.Send(new ClockRequest(id));
        }
    }

    // A blank or non-numeric input delay counts as 0 (in the original it made every note fire at once).
    private static double DelayNumber(string value) => AhkValue.Number(value) ?? 0;

    /// <summary>F3 (and "Stop" in a client's chat): stops everyone and resets the instruments' octaves.</summary>
    public Task Stop()
    {
        CancelPlaylist();
        List<PeerConnection> clients;
        lock (_gate)
        {
            _current?.Cancel();
            _current = null;
            clients = _slots.Where(c => c is not null).Select(c => c!).ToList();
        }
        _view.ShowMainWindow();
        foreach (var c in clients) c.Send(new StopPlayback());
        return ResetOctavesAsync();
    }

    /// <summary>F2: pauses/resumes the host's own playback (clients keep playing; on resume the host catches up).</summary>
    public bool TogglePause()
    {
        lock (_gate)
        {
            _paused = !_paused;
            _current?.Loop?.SetPaused(_paused);
            return _paused;
        }
    }

    /// <summary>The original RefreshPlayers (9, 9, 0 to Player1..5), with OctaveDelay ms before each round
    /// (the original always waited 150 ms).</summary>
    /// <remarks>Octave reset on the host's windows, OctaveDelay ms between rounds.</remarks>
    public Task ResetOctavesAsync() => _keys.ResetOctavesAsync(Timings.OctaveResetDelayMs ?? Config.AppSettings.OctaveResetDelay(OctaveDelay()));

    // ------------------------------------------------------------------ ping / sync

    private PingTracker PingTracker { get; } = new();

    /// <summary>
    /// Measures pings (run when Port Mapping opens): for each connected client in slot order, 50 probes 1 s apart; the displayed value is the
    /// floor of the mean one-way time (half the round trip) after removing outliers. Each client is measured on its
    /// own samples (the original pooled samples across clients and button presses).
    /// </summary>
    public async Task PingAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _pingBusy, 1) != 0) return;
        try
        {
            for (var slot = 2; slot <= PortMapping.Slots; slot++)
            {
                PeerConnection? conn;
                lock (_gate) conn = _slots[slot];
                if (conn is null) continue;
                PingTracker.Begin(slot);
                for (var i = 0; i < Timings.PingCount; i++)
                {
                    int id;
                    lock (_gate) id = ++_nextId;
                    PingTracker.Sent(slot, id, _clock.NowMs);
                    conn.Send(new Ping(id));
                    await Task.Delay(Timings.PingIntervalMs, ct).ConfigureAwait(false);
                }
                var value = Statistics.PingEstimate(PingTracker.Samples(slot)).ToString(CultureInfo.InvariantCulture);
                _view.SetClientPing(slot, value);
            }
        }
        finally { Volatile.Write(ref _pingBusy, 0); }
    }

    /// <summary>
    /// The Sync button: measures the host's own input delay (slot 1), then asks each connected client to measure
    /// theirs (they report back with <see cref="InputDelayReport"/>; until then their value is blank).
    /// </summary>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _syncBusy, 1) != 0) return;
        try
        {
            for (var slot = 1; slot <= PortMapping.Slots; slot++)
            {
                PeerConnection? conn;
                lock (_gate) conn = _slots[slot];
                if (slot != 1 && conn is null) continue;
                await Task.Delay(Timings.SyncPreDelayMs, ct).ConfigureAwait(false);
                string value;
                if (slot == 1)
                {
                    await _view.ShowMessageAndWaitAsync("Open Gw2, then press OK. The game is brought to the front for the test.").ConfigureAwait(false);
                    long? mean = null;
                    try
                    {
                        mean = InputDelay.Mean(await _meter.MeasureAsync(ct).ConfigureAwait(false));
                        if (mean is null) _view.ReportProblem("No sound was detected, so the host's input delay was not changed.");
                    }
                    catch (InputDelayException ex)
                    {
                        _view.ReportProblem(ex.Message + " The host's input delay was not changed.");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Whatever goes wrong here (no audio device, ...), the players must still be asked below.
                        _view.ReportProblem("Could not measure the host's input delay: " + ex.Message + " It was not changed.");
                    }
                    value = mean?.ToString(CultureInfo.InvariantCulture) ?? InputDelayOf(1);
                }
                else
                {
                    conn!.Send(new SyncInstrument());
                    var name = PlayerOf(slot).Name;
                    _view.AppendDialog($"Sync: asked {(string.IsNullOrWhiteSpace(name) ? "player " + slot : name)} to measure their input delay.");
                    value = "";
                }
                lock (_gate) _inputDelays[slot] = value;
                _view.SetInputDelay(slot, value);
            }
        }
        finally { Volatile.Write(ref _syncBusy, 0); }
    }

    public void Dispose()
    {
        _runner?.Cancel();
        lock (_gate)
        {
            _current?.Cancel();
            _current = null;
        }
        _listener?.Stop();
        List<PeerConnection> all;
        lock (_gate) all = _connections.ToList();
        foreach (var c in all) c.Close();
    }

    /// <summary>State of one song start: its cancellation, the clock samples and the running loop.</summary>
    private sealed class SongRun
    {
        private readonly CancellationTokenSource _cts = new();
        public readonly ClockSampler Clocks = new(PortMapping.Slots);
        public double T1 { get => Clocks.T1; set => Clocks.T1 = value; }
        public double?[] Ticks => Clocks.Ticks;
        public PlaybackLoop? Loop;
        public CancellationToken Token => _cts.Token;
        /// <summary>When the last note of any part is due, on the host's clock (set when the song starts).</summary>
        public double EndMs { get; set; }

        public void ExpectClock(int slot, int id, double sent) => Clocks.Sent(slot, id, sent);
        public void OnClockReply(int slot, int id, double tick, double now) => Clocks.Reply(slot, id, tick, now);

        public void Cancel()
        {
            _cts.Cancel();
            Loop?.Stop();
        }
    }
}

/// <summary>A player slot's occupant: this computer (slot 1), a connected client (with the name it announced, if
/// any yet), or nobody.</summary>
public sealed record PlayerInfo(int Slot, bool Connected, bool IsHost, string? Name, string? Address);

/// <summary>The host's fixed waits, all taken from the original scripts (tests shorten them).</summary>
public sealed record HostTimings
{
    /// <summary>Time allowed for clients to report their clocks before parts are sent (the original Sleep, 2000).</summary>
    public int ClockWaitMs { get; init; } = 2000;
    /// <summary>Clock probes per client and song (the original sent one); the fastest round trip wins.</summary>
    public int ClockProbes { get; init; } = 8;
    public int ClockProbeIntervalMs { get; init; } = 50;
    /// <summary>Pause before acting on a client's play request (Sleep, 250).</summary>
    public int ClientPlayRequestDelayMs { get; init; } = 250;
    /// <summary>Overrides the pause before each octave-reset round (tests); null = the host's OctaveDelay (150 ms if blank).</summary>
    public int? OctaveResetDelayMs { get; init; }
    public int PingCount { get; init; } = 50;
    public int PingIntervalMs { get; init; } = 1000;
    /// <summary>Pause before each Sync measurement (Sleep, 150).</summary>
    public int SyncPreDelayMs { get; init; } = 150;
    /// <summary>Playlists: time after a song's last note before it counts as over (lets it ring out).</summary>
    public int SongTailMs { get; init; } = 1500;
    /// <summary>Playlists: pause between the octave reset after one song and the start of the next.</summary>
    public int PlaylistGapMs { get; init; } = 1500;
}

/// <summary>Round-trip bookkeeping for the Ping button.</summary>
internal sealed class PingTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<int, double> _sent = new();
    private readonly Dictionary<int, List<double>> _samples = new();

    public void Begin(int slot) { lock (_gate) _samples[slot] = new List<double>(); }
    public void Sent(int slot, int id, double now) { lock (_gate) _sent[id] = now; }

    public void OnPong(int slot, int id, double now)
    {
        lock (_gate)
            if (_sent.Remove(id, out var t) && _samples.TryGetValue(slot, out var list))
                list.Add((now - t) / 2);
    }

    public IReadOnlyList<double> Samples(int slot) { lock (_gate) return _samples.GetValueOrDefault(slot)?.ToList() ?? new List<double>(); }
}
