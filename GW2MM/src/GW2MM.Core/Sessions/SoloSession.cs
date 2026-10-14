using GW2MM.Core.Arrangement;
using GW2MM.Core.Humanizing;
using GW2MM.Core.Library;
using GW2MM.Core.Playback;
using GW2MM.Core.Text;

namespace GW2MM.Core.Sessions;

/// <summary>
/// A client playing on its own while it is not connected to a host (not in the original): songs and playlists from
/// its library play on the Guild Wars 2 windows open on this computer. The song's tracks that contain notes are given,
/// in order, to the open Player windows (lowest number first): the 1st track to the 1st open window, and so on. This is
/// the same arrangement a host gives a client that is assigned those tracks. Tracks beyond the number of open windows
/// are not played.
/// </summary>
public sealed class SoloSession : IDisposable
{
    /// <summary>Time from pressing Play to the first note.</summary>
    public const double LeadMs = 500;

    private readonly object _gate = new();
    private readonly ISessionView _view;
    private readonly IClock _clock;
    private readonly IKeyOutput _keys;
    private readonly Func<IReadOnlyList<int>> _openWindows;
    private readonly Func<string> _octaveDelay;
    private readonly Func<int> _octaveDelayMs;
    private readonly PlaylistRunner _runner;
    private readonly Func<HumanizeLevel> _humanize;
    private readonly Random _random;
    private PlaybackLoop? _loop;
    private string? _lastWhere;

    /// <param name="openWindows">The numbers n of the open game windows titled "Player&lt;n&gt;", ascending.</param>
    /// <param name="octaveDelay">OctaveDelay text for the arrangement (as the host uses it).</param>
    /// <param name="octaveDelayMs">Pause between octave-reset presses (as the client uses it after a Stop).</param>
    public SoloSession(ISessionView view, SongLibrary library, Func<IReadOnlyList<int>> openWindows, Func<string> octaveDelay,
        Func<int> octaveDelayMs, IClock clock, IKeyOutput keys, int tailMs = 1500, int gapMs = 1500,
        Func<HumanizeLevel>? humanize = null, Random? random = null)
    {
        _humanize = humanize ?? (() => HumanizeLevel.Off);
        _random = random ?? new Random();
        _view = view;
        Library = library;
        _openWindows = openWindows;
        _octaveDelay = octaveDelay;
        _octaveDelayMs = octaveDelayMs;
        _clock = clock;
        _keys = keys;
        _runner = new PlaylistRunner(clock,
            start: song => Task.FromResult(Start(song.Csv, song.Tempo, song.SingleInstrument)),
            end: EndSongAsync,
            announce: info => { _view.AppendDialog(info.Notice); _view.OnNowPlaying(info); },
            ended: (name, _, finished) =>
            {
                if (finished) _view.AppendDialog($"♪ Playlist {name} finished.");
                _view.OnNowPlaying(null);
            },
            report: _view.ReportProblem)
        {
            TailMs = tailMs,
            GapMs = gapMs,
        };
    }

    public SongLibrary Library { get; }
    public bool IsPlaying { get { lock (_gate) return _loop is { Completion.IsCompleted: false }; } }
    public bool IsPlaylistRunning => _runner.IsRunning;

    /// <summary>Play (Songs tab): plays one song from the library on this computer.</summary>
    public void PlaySong(string name)
    {
        CancelPlaylist();
        if (Library.ReadSongCsv(name) is not { } csv)
        {
            _view.ReportProblem(name.Length == 0 ? "Select a song first." : $"The song {name} is not in the library.");
            return;
        }
        if (Start(csv, Library.TempoFactor(name), Library.IsSingleInstrument(name)) is null) return;
        var info = new NowPlayingInfo(name, null, 0, 0);
        _view.AppendDialog(info.Notice);
        _view.OnNowPlaying(info);
    }

    /// <summary>Play (Playlists tab): plays a playlist on this computer.</summary>
    public Task PlayPlaylistAsync(Playlist playlist, int startIndex, bool shuffle, bool repeat)
    {
        var (songs, first) = QueuedSongs.FromLibrary(Library, playlist, startIndex, shuffle, _view.ReportProblem);
        return _runner.RunAsync(playlist.Name, songs, first, repeat);
    }

    public bool NextSong() => _runner.Next();

    /// <summary>Stop: ends the song and playlist and resets the octaves.</summary>
    public Task StopAsync()
    {
        CancelPlaylist();
        lock (_gate)
        {
            _loop?.Stop();
            _loop = null;
        }
        return _keys.ResetOctavesAsync(_octaveDelayMs());
    }

    /// <summary>Stops without resetting octaves (the client connected to a host, which takes over).</summary>
    public void Cancel()
    {
        CancelPlaylist();
        lock (_gate)
        {
            _loop?.Stop();
            _loop = null;
        }
    }

    private void CancelPlaylist()
    {
        if (_runner.Cancel()) _view.OnNowPlaying(null);
    }

    /// <summary>Arranges and starts a song on the open windows; returns when its last note is due, or null.</summary>
    private double? Start(string csvText, double? tempoFactor, bool singleInstrument = false)
    {
        lock (_gate)
        {
            _loop?.Stop();
            _loop = null;
        }
        var windows = _openWindows();
        if (windows.Count == 0)
        {
            _view.ReportProblem("No Guild Wars 2 windows are named Player1…Player8. Focus each game window and press Shift+F1, Shift+F2, … to name it.");
            return null;
        }

        SlotPlan part;
        SoloScore? score = null;
        var humanize = _humanize();
        try
        {
            var song = CsvSong.Parse(LegacyText.Lines(csvText));
            // A one-player song: every track on one instrument, in the first open window.
            var tracks = singleInstrument ? new List<int> { 1 } : Enumerable.Range(1, PortMapping.Tracks).Where(t => song.TrackNotes[t].Count > 0).ToList();
            if (tracks.Count == 0)
            {
                _view.ReportProblem("The song has no notes to play.");
                return null;
            }
            if (tracks.Count > windows.Count)
                _view.AppendDialog(windows.Count == 1
                    ? $"This song has {tracks.Count} parts but only 1 game window is open, so only the first part is played."
                    : $"This song has {tracks.Count} parts but only {windows.Count} game windows are open, so only the first {windows.Count} parts are played.");
            var mapping = new PortMapping();
            foreach (var t in tracks.Take(windows.Count)) mapping[SoloSlot, t] = true;
            part = SongArranger.Arrange(song, mapping, _octaveDelay(), tempoFactor, singleInstrument: singleInstrument)[SoloSlot];
            // Humanizing plays the same presses (notes and octave changes) with its own timing.
            if (humanize != HumanizeLevel.Off && part.IsActive)
                score = singleInstrument ? SoloScore.OnePlayer(song, tempoFactor) : SoloScore.FromPart(song, part, tempoFactor, tracks.Take(windows.Count).ToList());
        }
        catch (Exception ex)
        {
            _view.ReportProblem("Could not prepare the song: " + ex.Message);
            return null;
        }
        if (!part.IsActive)
        {
            _view.ReportProblem("The song has too few notes to play.");
            return null;
        }

        var where = "Not connected: playing on " + string.Join(", ", windows.Select(KeyMap.WindowTitle)) + ".";
        if (where != _lastWhere) _view.AppendDialog(where); // once, and again when the open windows change
        _lastWhere = where;
        // Anchored like a client's part: the first element plays at once, then each at its time.
        var start = _clock.NowMs + LeadMs;
        var times = Schedule.AbsoluteTimes(part, start - Schedule.StartDelayMs, 0);
        var events = Schedule.ClientEvents(part.Notes, times.Select(t => (double?)t).ToList());
        if (score is not null)
        {
            events = Humanizer.Perform(score, SingleInstrument.DelayMs(_octaveDelay()), humanize, _random, start);
            times = events.Select(e => e.DueMs ?? start).ToList();
        }
        var loop = new PlaybackLoop(events, _clock, new WindowRemap(_keys, windows));
        loop.KeyError += ex => _view.ReportProblem("Key press failed: " + ex.Message);
        lock (_gate)
        {
            _loop = loop;
            loop.Start();
        }
        return times.Count == 0 ? _clock.NowMs : times[^1];
    }

    /// <summary>The arrangement for a client slot sends its k-th track to window k.</summary>
    private const int SoloSlot = 2;

    private Task EndSongAsync()
    {
        lock (_gate)
        {
            _loop?.Stop();
            _loop = null;
        }
        return _keys.ResetOctavesAsync(_octaveDelayMs());
    }

    public void Dispose() => Cancel();

    /// <summary>Sends the k-th part's keys to the k-th open window (so Player1 + Player3 open plays parts 1 and 2 there).</summary>
    private sealed class WindowRemap(IKeyOutput inner, IReadOnlyList<int> windows) : IKeyOutput
    {
        public void Press(int window, string key)
        {
            if (window >= 1 && window <= windows.Count) inner.Press(windows[window - 1], key);
        }
    }
}
