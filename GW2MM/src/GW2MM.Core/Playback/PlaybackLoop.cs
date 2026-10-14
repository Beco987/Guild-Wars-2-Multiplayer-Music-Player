namespace GW2MM.Core.Playback;

/// <summary>
/// The song-playing loop (the original <c>SongPlayer1</c> timer on host and client). Walks the events in order on a
/// dedicated high-priority thread: waits until each event's due time, then presses its keys.
/// <list type="bullet">
/// <item>Events whose time has already passed fire immediately, in order, at least <see cref="MinGapMs"/> apart -
/// the original timer pressed at most one element per tick and caught up the same way.</item>
/// <item>While paused nothing is pressed; on resume overdue events are caught up (as the original's paused timers did).</item>
/// <item><see cref="Stop"/> takes effect before the next key press. The loop never restarts or repeats on its own.</item>
/// </list>
/// </summary>
public sealed class PlaybackLoop
{
    public const double MinGapMs = 1.0;

    private readonly IReadOnlyList<PlaybackEvent> _events;
    private readonly IClock _clock;
    private readonly IKeyOutput _output;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _running = new(true);
    private readonly TaskCompletionSource<PlaybackOutcome> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;

    public PlaybackLoop(IReadOnlyList<PlaybackEvent> events, IClock clock, IKeyOutput output, bool startPaused = false)
    {
        _events = events;
        _clock = clock;
        _output = output;
        if (startPaused) _running.Reset();
    }

    /// <summary>Raised on the playback thread when a key press throws; playback continues with the next event.</summary>
    public event Action<Exception>? KeyError;

    public Task<PlaybackOutcome> Completion => _done.Task;
    public int Position { get; private set; }
    public bool IsPaused => !_running.IsSet;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Already started.");
        var thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "GW2MM playback" };
        thread.Start();
    }

    public void Stop() => _cts.Cancel();

    public void SetPaused(bool paused)
    {
        if (paused) _running.Reset(); else _running.Set();
    }

    private void Run()
    {
        var outcome = PlaybackOutcome.Completed;
        try
        {
            var lastPress = double.NegativeInfinity;
            for (var i = 0; i < _events.Count; i++)
            {
                Position = i;
                var e = _events[i];
                var due = e.DueMs ?? double.NegativeInfinity;
                if (e.Keys.Length > 0) due = Math.Max(due, lastPress + MinGapMs);
                if (!_clock.WaitUntil(due, _cts.Token)) { outcome = PlaybackOutcome.Stopped; break; }
                if (!_running.IsSet)
                {
                    try { _running.Wait(_cts.Token); }
                    catch (OperationCanceledException) { outcome = PlaybackOutcome.Stopped; break; }
                }
                if (_cts.IsCancellationRequested) { outcome = PlaybackOutcome.Stopped; break; }
                if (e.Keys.Length == 0) continue;
                try { _output.PressKeys(e.Keys); }
                catch (Exception ex) { KeyError?.Invoke(ex); }
                lastPress = _clock.NowMs;
            }
            Position = _events.Count;
        }
        finally
        {
            _done.TrySetResult(outcome);
        }
    }
}

public enum PlaybackOutcome { Completed, Stopped }
