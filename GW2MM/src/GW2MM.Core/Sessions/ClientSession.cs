using System.Globalization;
using GW2MM.Core.Library;
using GW2MM.Core.Net;
using GW2MM.Core.Playback;

namespace GW2MM.Core.Sessions;

/// <summary>
/// The client side: connects to the host, answers clock and ping probes immediately, plays the parts the host sends
/// (in this machine's clock), stops and resets octaves when told to, measures its input delay on request and can ask
/// the host to play a song from this machine's library.
/// </summary>
public sealed class ClientSession : IDisposable
{
    private readonly object _gate = new();
    private readonly ISessionView _view;
    private readonly IClock _clock;
    private readonly IKeyOutput _keys;
    private readonly IInputDelayMeter _meter;
    private readonly Func<int> _octaveDelayMs;
    private PeerConnection? _conn;
    private PlaybackLoop? _loop;
    private bool _lastTyping;
    private string _name = "";
    private int _syncBusy;

    public ClientSession(ISessionView view, SongLibrary library, Func<int> octaveDelayMs, IClock clock, IKeyOutput keys, IInputDelayMeter meter)
    {
        _view = view;
        Library = library;
        _octaveDelayMs = octaveDelayMs;
        _clock = clock;
        _keys = OctaveTracker.Of(keys); // shared with whatever else plays on these windows
        _meter = meter;
    }

    public SongLibrary Library { get; }
    /// <summary>Pause between closing the Sync message box and the first test press (the original Sleep, 150).</summary>
    public int SyncPreDelayMs { get; init; } = 150;
    public int? Slot { get; private set; }

    /// <summary>
    /// Answer the host's offer to follow this computer's clock over UDP (<see cref="ClockSyncClient"/>). Tests turn
    /// this off to check the TCP probes the host falls back to.
    /// </summary>
    public bool UseUdpClock { get; init; } = true;
    private ClockSyncClient? _clockSync;
    public bool IsConnected { get { lock (_gate) return _conn is not null; } }
    public bool IsPlaying { get { lock (_gate) return _loop is { Completion.IsCompleted: false }; } }

    /// <summary>Connects and announces <paramref name="name"/>. Failure leaves the window open with "Failed to Connect...".</summary>
    public async Task<bool> ConnectAsync(string host, string name, int port = PeerConnection.DefaultPort)
    {
        _view.SetStatus($"Trying to connect to {host}...");
        PeerConnection conn;
        try { conn = await PeerConnection.ConnectAsync(host, port).ConfigureAwait(false); }
        catch (Exception)
        {
            _view.SetStatus("Failed to Connect...");
            return false;
        }
        lock (_gate)
        {
            _conn = conn;
            _name = name.Trim();
            if (_name.Length > 40) _name = _name[..40]; // the host's limit
        }
        conn.Received += OnReceived;
        conn.Closed += OnClosed;
        conn.Start();
        conn.Send(new Hello(name));
        _view.SetChatEnabled(true);
        _view.SetStatus($"Connected to {host}!");
        return true;
    }

    /// <summary>Starts sending UDP clock requests to the host's port, when the host offered it.</summary>
    private void StartClockSync(PeerConnection conn, string? token)
    {
        if (!UseUdpClock || token is null || conn.RemoteEndPoint is not { } host
            || !ulong.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return;
        try
        {
            var sync = new ClockSyncClient(host, value, _clock);
            lock (_gate)
            {
                _clockSync?.Dispose();
                _clockSync = sync;
            }
            sync.Start();
        }
        catch (System.Net.Sockets.SocketException ex) { _view.LogEvent("Clock: UDP clock requests could not start: " + ex.Message); }
    }

    private void StopClockSync()
    {
        ClockSyncClient? sync;
        lock (_gate) { sync = _clockSync; _clockSync = null; }
        sync?.Dispose();
    }

    private void OnClosed(PeerConnection conn)
    {
        StopClockSync();
        lock (_gate) if (ReferenceEquals(_conn, conn)) _conn = null;
        // A part that is already scheduled keeps playing, as in the original.
        _view.SetStatus("Disconnected!");
        _view.SetChatEnabled(false);
    }

    private void Send(Message m)
    {
        PeerConnection? conn;
        lock (_gate) conn = _conn;
        conn?.Send(m);
    }

    private void OnReceived(PeerConnection conn, Message message)
    {
        switch (message)
        {
            case ClockRequest r:
                // Answer first, from the network thread, so the reported clock is as fresh as possible.
                conn.Send(new ClockReply(r.Id, _clock.NowMs));
                _view.LogEvent("Tick_Count");
                break;
            case Ping p:
                conn.Send(new Pong(p.Id));
                _view.LogEvent("Ping_Server");
                break;
            case SongPart s:
                _view.LogEvent($"Rest_1 ({s.Rests.Length} times)");
                _view.LogEvent($"Note_1 ({s.Notes.Length} notes)");
                StartPart(s);
                break;
            case StopPlayback stop:
                _view.LogEvent("Stop");
                if (stop.KeepOctaves) StopPlaying();
                else _ = StopAsync();
                break;
            case Chat c:
                // Display only: chat text never triggers anything on a client.
                _view.AppendDialog(ChatRules.Line(string.IsNullOrWhiteSpace(c.From) ? ChatRules.HostName : c.From, c.Text));
                break;
            case NowPlaying np:
                if (np.Song is not null)
                {
                    var info = new NowPlayingInfo(np.Song, np.Playlist, np.Index, np.Count);
                    _view.AppendDialog(info.Notice);
                    _view.OnNowPlaying(info);
                }
                else
                {
                    if (np.Finished && np.Playlist is not null) _view.AppendDialog($"♪ Playlist {np.Playlist} finished.");
                    _view.OnNowPlaying(null);
                }
                break;
            case Welcome w:
                Slot = w.Slot;
                StartClockSync(conn, w.ClockToken);
                _view.LogEvent("Socket,Socket" + (w.Slot?.ToString(CultureInfo.InvariantCulture) ?? ""));
                break;
            case SyncInstrument:
                _view.LogEvent("Sync_Instrument");
                _view.AppendDialog("Sync: the host asked for your input delay.");
                _ = SyncAsync();
                break;
        }
    }

    /// <summary>A new part replaces whatever is playing (the original reset its single timer and index).</summary>
    private void StartPart(SongPart part)
    {
        var loop = new PlaybackLoop(Schedule.ClientEvents(part.Notes, part.Rests), _clock, _keys,
            octaveTimerMs: part.OctaveTimer ? _octaveDelayMs() : null);
        loop.KeyError += ex => _view.ReportProblem("Key press failed: " + ex.Message);
        lock (_gate)
        {
            _loop?.Stop();
            _loop = loop;
            loop.Start();
        }
        // A part that plays to its end returns the octaves to the middle one (a stopped one is reset by the Stop).
        if (!part.KeepOctaves)
            _ = loop.Completion.ContinueWith(t => t.Result == PlaybackOutcome.Completed ? ResetOctavesAsync() : Task.CompletedTask,
                TaskScheduler.Default).Unwrap();
    }

    /// <summary>"Stop" from the host: stop playing and reset octaves, waiting OctaveDelay ms before each of 9, 9, 0.</summary>
    public Task StopAsync()
    {
        StopPlaying();
        return ResetOctavesAsync();
    }

    /// <summary>Stops the part that is playing, leaving the octaves as they are.</summary>
    public void StopPlaying()
    {
        lock (_gate)
        {
            _loop?.Stop();
            _loop = null;
        }
    }

    public Task ResetOctavesAsync() => _keys.ResetOctavesAsync(_octaveDelayMs());

    private async Task SyncAsync()
    {
        if (Interlocked.Exchange(ref _syncBusy, 1) != 0) return;
        try
        {
            await _view.ShowMessageAndWaitAsync("Open Gw2, turn all ingame volumes to 0 EXCEPT for player instruments, then press OK. The game is brought to the front for the test.").ConfigureAwait(false);
            await Task.Delay(SyncPreDelayMs).ConfigureAwait(false);
            long? mean;
            try { mean = InputDelay.Mean(await _meter.MeasureAsync(CancellationToken.None).ConfigureAwait(false)); }
            catch (InputDelayException ex)
            {
                _view.ReportProblem(ex.Message + " The host was sent a blank input delay.");
                Send(new InputDelayReport(null));
                return;
            }
            if (mean is null) _view.ReportProblem("No sound was detected; the host was sent a blank input delay.");
            Send(new InputDelayReport(mean));
        }
        catch (Exception ex) { _view.ReportProblem("Input delay measurement failed: " + ex.Message); }
        finally { Volatile.Write(ref _syncBusy, 0); }
    }

    public void SendChat(string text)
    {
        if (string.IsNullOrEmpty(text) || !IsConnected) return;
        Send(new Chat(text));
        _view.AppendDialog(ChatRules.Line(string.IsNullOrWhiteSpace(_name) ? "You" : _name, text));
    }

    /// <summary>Chat box text changed: tell the host when typing starts/stops (only on change).</summary>
    public void TypingChanged(string text)
    {
        var typing = text.Length > 0;
        lock (_gate)
        {
            if (_conn is null || typing == _lastTyping) return;
            _lastTyping = typing;
        }
        Send(new Typing(typing));
    }

    /// <summary>F1 / the Play button: asks the host to play the selected song from this machine's library.</summary>
    public void RequestPlay(string? songName)
    {
        var name = songName ?? "";
        var csv = Library.ReadSongCsv(name) ?? "";
        var tempo = Library.TempoFactor(name).ToString("0.000000", CultureInfo.InvariantCulture);
        Send(new PlayRequest(csv, tempo, name.Length > 0 ? name : null, Library.IsSingleInstrument(name)));
    }

    /// <summary>
    /// Asks the host to play one of this library's playlists for everyone (the client's F1 / Play on the Playlists
    /// tab). Songs missing from the library are skipped. Returns false when nothing could be sent.
    /// </summary>
    public bool RequestPlaylist(Playlist playlist, int startIndex, bool shuffle, bool repeat)
    {
        if (!IsConnected) return false;
        var songs = new List<PlayRequest>();
        var missing = new List<string>();
        foreach (var name in playlist.Songs)
        {
            if (Library.ReadSongCsv(name) is not { } csv) { missing.Add(name); continue; }
            songs.Add(new PlayRequest(csv, Library.TempoFactor(name).ToString("0.000000", CultureInfo.InvariantCulture), name, Library.IsSingleInstrument(name)));
        }
        if (missing.Count > 0) _view.ReportProblem($"Skipping songs that are not in the library: {string.Join(", ", missing)}.");
        if (songs.Count == 0)
        {
            _view.ReportProblem($"The playlist {playlist.Name} has no songs to play.");
            return false;
        }
        var startName = startIndex >= 0 && startIndex < playlist.Songs.Count ? playlist.Songs[startIndex] : null;
        var start = startName is null ? 0 : Math.Max(0, songs.FindIndex(s => s.Name == startName));
        var (ordered, first) = PlaylistOrder.Arrange(songs, start, shuffle);
        Send(new PlaylistRequest(playlist.Name, ordered.ToArray(), first, repeat));
        return true;
    }

    /// <summary>Settings → Disconnect: leaves the host (a part already scheduled is stopped).</summary>
    public void Disconnect()
    {
        PeerConnection? conn;
        lock (_gate)
        {
            _loop?.Stop();
            conn = _conn;
        }
        conn?.Close();
    }

    public void Dispose()
    {
        StopClockSync();
        PeerConnection? conn;
        lock (_gate)
        {
            _loop?.Stop();
            conn = _conn;
        }
        conn?.Close();
    }
}
