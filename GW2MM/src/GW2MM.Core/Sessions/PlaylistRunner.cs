using GW2MM.Core.Library;
using GW2MM.Core.Playback;

namespace GW2MM.Core.Sessions;

/// <summary>A song waiting in a playlist: its name (for notices), CSV text and tempo factor.</summary>
/// <param name="SingleInstrument">A one-player song: all tracks merged into one instrument.</param>
public sealed record QueuedSong(string Name, string Csv, double? Tempo, bool SingleInstrument = false);

public static class QueuedSongs
{
    /// <summary>
    /// A playlist's songs from a library, in playing order (see <see cref="PlaylistOrder"/>), and the index to start
    /// at. Songs that are not in the library are skipped and reported.
    /// </summary>
    public static (IReadOnlyList<QueuedSong> Songs, int Start) FromLibrary(SongLibrary library, Playlist playlist, int startIndex, bool shuffle, Action<string> report)
    {
        var songs = new List<QueuedSong>();
        var missing = new List<string>();
        foreach (var name in playlist.Songs)
        {
            if (library.ReadSongCsv(name) is { } csv) songs.Add(new QueuedSong(name, csv, library.TempoFactor(name), library.IsSingleInstrument(name)));
            else missing.Add(name);
        }
        if (missing.Count > 0) report($"Skipping songs that are not in the library: {string.Join(", ", missing)}.");
        // The start index refers to the playlist as shown; find the same song among the ones that exist.
        var startName = startIndex >= 0 && startIndex < playlist.Songs.Count ? playlist.Songs[startIndex] : null;
        var start = startName is null ? 0 : Math.Max(0, songs.FindIndex(s => s.Name == startName));
        return PlaylistOrder.Arrange(songs, start, shuffle);
    }
}

/// <summary>
/// Plays a list of songs one after another (used by the host for everyone, and by a client playing solo). For each
/// song it announces it, starts it, waits until its last note is due plus <see cref="TailMs"/>, ends it (stop
/// leftovers, reset octaves), pauses <see cref="GapMs"/> and moves on. <see cref="Next"/> skips the wait;
/// <see cref="Cancel"/> or a newer <see cref="RunAsync"/> ends it.
/// </summary>
public sealed class PlaylistRunner
{
    private readonly object _gate = new();
    private readonly IClock _clock;
    private readonly Func<QueuedSong, Task<double?>> _start;
    private readonly Func<Task> _end;
    private readonly Action<NowPlayingInfo> _announce;
    private readonly Action<string, int, bool> _ended;
    private readonly Action<string> _report;
    private CancellationTokenSource? _playlist;
    private CancellationTokenSource? _songWait;

    /// <param name="start">Starts a song; returns when its last note is due (on <paramref name="clock"/>), or null if it did not start.</param>
    /// <param name="end">Ends the song that just played (stop what is left of it, reset octaves).</param>
    /// <param name="announce">A song is about to start.</param>
    /// <param name="ended">The playlist ended by itself (name, song count, finished = played to the end rather than failed).</param>
    public PlaylistRunner(IClock clock, Func<QueuedSong, Task<double?>> start, Func<Task> end, Action<NowPlayingInfo> announce,
        Action<string, int, bool> ended, Action<string> report)
    {
        _clock = clock;
        _start = start;
        _end = end;
        _announce = announce;
        _ended = ended;
        _report = report;
    }

    public int TailMs { get; init; } = 1500;
    public int GapMs { get; init; } = 1500;

    public bool IsRunning { get { lock (_gate) return _playlist is not null; } }

    public async Task RunAsync(string name, IReadOnlyList<QueuedSong> songs, int startIndex, bool repeat)
    {
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _playlist?.Cancel();
            _playlist = cts;
        }
        var ct = cts.Token;
        var finished = false;
        try
        {
            if (songs.Count == 0)
            {
                _report($"The playlist {name} has no songs to play.");
                return;
            }
            var from = Math.Clamp(startIndex, 0, songs.Count - 1);
            while (true)
            {
                var played = 0;
                for (var i = from; i < songs.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var song = songs[i];
                    _announce(new NowPlayingInfo(song.Name, name, i + 1, songs.Count));
                    var end = await _start(song).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (end is null) continue; // could not be prepared (already reported): go on with the next one
                    played++;

                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lock (_gate) _songWait = wait;
                    try
                    {
                        var remaining = end.Value + TailMs - _clock.NowMs;
                        if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), wait.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { } // Next
                    finally { lock (_gate) if (_songWait == wait) _songWait = null; }
                    ct.ThrowIfCancellationRequested();

                    await _end().ConfigureAwait(false);
                    if (i < songs.Count - 1 || repeat) await Task.Delay(GapMs, ct).ConfigureAwait(false);
                }
                if (!repeat || played == 0) break;
                from = 0;
            }
            finished = true;
        }
        catch (OperationCanceledException) { }
        finally
        {
            bool stillOurs;
            lock (_gate)
            {
                stillOurs = _playlist == cts;
                if (stillOurs) _playlist = null;
            }
            if (stillOurs) _ended(name, songs.Count, finished);
            cts.Dispose();
        }
    }

    /// <summary>Ends the current song now and moves on. False when no playlist is playing.</summary>
    public bool Next()
    {
        lock (_gate)
        {
            if (_playlist is null || _songWait is null) return false;
            _songWait.Cancel();
            return true;
        }
    }

    /// <summary>Stops the playlist (the caller reports it). False when none was running.</summary>
    public bool Cancel()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _playlist;
            _playlist = null;
        }
        if (cts is null) return false;
        cts.Cancel();
        return true;
    }
}
