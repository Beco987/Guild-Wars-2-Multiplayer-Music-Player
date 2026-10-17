namespace GW2MM.Core.Playback;

/// <summary>
/// The song-playing loop (the original <c>SongPlayer1</c> timer on host and client). Walks the events in order on a
/// dedicated high-priority thread: waits until each event's due time, then presses its keys.
/// <list type="bullet">
/// <item>Events whose time has already passed fire immediately, in order, at least <see cref="MinGapMs"/> apart -
/// the original timer pressed at most one element per tick and caught up the same way.</item>
/// <item>While paused nothing is pressed; on resume overdue events are caught up (as the original's paused timers did).</item>
/// <item><see cref="Stop"/> takes effect before the next key press. The loop never restarts or repeats on its own.</item>
/// <item><see cref="SetSpeed"/> (not in the original) re-times the events still to come around the current moment, so
/// the song continues faster or slower from where it is.</item>
/// </list>
/// </summary>
public sealed class PlaybackLoop
{
    public const double MinGapMs = 1.0;

    private readonly IReadOnlyList<PlaybackEvent> _events;
    private readonly double?[] _due;
    private readonly object _timing = new();
    private CancellationTokenSource _retimed = new();
    private double _speed = 1;
    private readonly IClock _clock;
    private readonly IKeyOutput _output;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _running = new(true);
    private readonly TaskCompletionSource<PlaybackOutcome> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;

    public PlaybackLoop(IReadOnlyList<PlaybackEvent> events, IClock clock, IKeyOutput output, bool startPaused = false)
    {
        _events = events;
        _due = events.Select(e => e.DueMs).ToArray();
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

    /// <summary>The current playback speed (1 = as scheduled).</summary>
    public double Speed { get { lock (_timing) return _speed; } }

    /// <summary>When the last event is now due (after any speed change); null when nothing is scheduled.</summary>
    public double? LastDueMs { get { lock (_timing) return _due.Where(d => d is not null).DefaultIfEmpty().Max(); } }

    /// <summary>
    /// Plays the rest of the song at <paramref name="speed"/> times the scheduled tempo (1.1 = 10% faster): every event
    /// not yet played keeps its distance from now, divided by the change in speed.
    /// </summary>
    public void SetSpeed(double speed)
    {
        if (speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
        CancellationTokenSource old;
        lock (_timing)
        {
            var now = _clock.NowMs;
            var factor = _speed / speed;
            for (var i = Position; i < _due.Length; i++)
                if (_due[i] is { } d && d > now) _due[i] = now + (d - now) * factor;
            _speed = speed;
            old = _retimed;
            _retimed = new CancellationTokenSource();
        }
        old.Cancel(); // wakes the loop so it waits for the new time
    }

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
                double due;
                CancellationToken retimed;
                lock (_timing)
                {
                    due = _due[i] ?? double.NegativeInfinity;
                    retimed = _retimed.Token;
                }
                if (e.Keys.Length > 0) due = Math.Max(due, lastPress + MinGapMs);
                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, retimed))
                    if (!_clock.WaitUntil(due, wait.Token))
                    {
                        if (_cts.IsCancellationRequested) { outcome = PlaybackOutcome.Stopped; break; }
                        i--; // the speed changed while waiting: wait again for the event's new time
                        continue;
                    }
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
