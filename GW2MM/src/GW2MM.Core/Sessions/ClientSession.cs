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
    private int _syncBusy;

    public ClientSession(ISessionView view, SongLibrary library, Func<int> octaveDelayMs, IClock clock, IKeyOutput keys, IInputDelayMeter meter)
    {
        _view = view;
        Library = library;
        _octaveDelayMs = octaveDelayMs;
        _clock = clock;
        _keys = keys;
        _meter = meter;
    }

    public SongLibrary Library { get; }
    /// <summary>Pause between closing the Sync message box and the first test press (the original Sleep, 150).</summary>
    public int SyncPreDelayMs { get; init; } = 150;
    public int? Slot { get; private set; }
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
        lock (_gate) _conn = conn;
        conn.Received += OnReceived;
        conn.Closed += OnClosed;
        conn.Start();
        conn.Send(new Hello(name));
        _view.SetChatEnabled(true);
        _view.SetStatus($"Connected to {host}!");
        return true;
    }

    private void OnClosed(PeerConnection conn)
    {
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
                _view.AppendDialog("Peer > Tick_Count");
                break;
            case Ping p:
                conn.Send(new Pong(p.Id));
                _view.AppendDialog("Peer > Ping_Server");
                break;
            case SongPart s:
                _view.AppendDialog($"Peer > Rest_1 ({s.Rests.Length} times)");
                _view.AppendDialog($"Peer > Note_1 ({s.Notes.Length} notes)");
                StartPart(s);
                break;
            case StopPlayback:
                _view.AppendDialog("Peer > Stop");
                _ = StopAsync();
                break;
            case Chat c:
                _view.AppendDialog("Peer > " + c.Text);
                if (c.Text.Contains("Stop", StringComparison.OrdinalIgnoreCase)) _ = StopAsync();
                break;
            case Welcome w:
                Slot = w.Slot;
                _view.AppendDialog("Peer > Socket,Socket" + (w.Slot?.ToString(CultureInfo.InvariantCulture) ?? ""));
                break;
            case SyncInstrument:
                _view.AppendDialog("Peer > Sync_Instrument");
                _ = SyncAsync();
                break;
        }
    }

    /// <summary>A new part replaces whatever is playing (the original reset its single timer and index).</summary>
    private void StartPart(SongPart part)
    {
        var loop = new PlaybackLoop(Schedule.ClientEvents(part.Notes, part.Rests), _clock, _keys);
        loop.KeyError += ex => _view.ReportProblem("Key press failed: " + ex.Message);
        lock (_gate)
        {
            _loop?.Stop();
            _loop = loop;
            loop.Start();
        }
    }

    /// <summary>"Stop" from the host: stop playing and reset octaves, waiting OctaveDelay ms before each of 9, 9, 0.</summary>
    public Task StopAsync()
    {
        lock (_gate)
        {
            _loop?.Stop();
            _loop = null;
        }
        return ResetOctavesAsync();
    }

    public Task ResetOctavesAsync() => _keys.ResetOctavesAsync(_octaveDelayMs());

    private async Task SyncAsync()
    {
        if (Interlocked.Exchange(ref _syncBusy, 1) != 0) return;
        try
        {
            await _view.ShowMessageAndWaitAsync("Open Gw2, turn all ingame volumes to 0 EXCEPT for player instruments, then press OK").ConfigureAwait(false);
            await Task.Delay(SyncPreDelayMs).ConfigureAwait(false);
            var mean = InputDelay.Mean(await _meter.MeasureAsync(CancellationToken.None).ConfigureAwait(false));
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
        _view.AppendDialog("You > " + text);
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

    /// <summary>F11 / the Play button: asks the host to play the selected song from this machine's library.</summary>
    public void RequestPlay(string? songName)
    {
        var name = songName ?? "";
        var csv = Library.ReadSongCsv(name) ?? "";
        var tempo = Library.TempoFactor(name).ToString("0.000000", CultureInfo.InvariantCulture);
        Send(new PlayRequest(csv, tempo));
    }

    public void Dispose()
    {
        PeerConnection? conn;
        lock (_gate)
        {
            _loop?.Stop();
            conn = _conn;
        }
        conn?.Close();
    }
}
