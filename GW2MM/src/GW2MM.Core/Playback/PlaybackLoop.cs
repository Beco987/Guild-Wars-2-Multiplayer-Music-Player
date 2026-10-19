using GW2MM.Core.Arrangement;

namespace GW2MM.Core.Playback;

/// <summary>
/// The song-playing loop (the original <c>SongPlayer1</c> timer on host and client). Walks the events in order on a
/// dedicated high-priority thread: waits until each event's due time, then presses its keys.
/// <list type="bullet">
/// <item>Events whose time has already passed fire immediately, in order, at least <see cref="MinGapMs"/> apart -
/// the original timer pressed at most one element per tick and caught up the same way.</item>
/// <item>While paused nothing is pressed. On resume the song carries on from where it was paused: everything still to
/// come moves later by the length of the pause. With <see cref="CatchUp"/> (a host whose players keep playing through
/// its pause) the missed events are caught up instead, as the original's paused timers did.</item>
/// <item><see cref="Stop"/> takes effect before the next key press. The loop never restarts or repeats on its own.</item>
/// <item><see cref="SetSpeed"/> (not in the original) re-times the events still to come around the current moment, so
/// the song continues faster or slower from where it is.</item>
/// <item>Live octave timer (test option, not in the original; see <see cref="OctaveTimer"/>).</item>
/// </list>
/// </summary>
public sealed class PlaybackLoop
{
    public const double MinGapMs = 1.0;

    /// <summary>The live octave timer counts in steps of this many ms.</summary>
    public const double OctaveTimerStepMs = 10;

    private readonly IReadOnlyList<PlaybackEvent> _events;
    private readonly string[] _keys;
    private readonly double? _octaveDelayMs;
    private readonly double?[] _due;
    private readonly object _timing = new();
    private CancellationTokenSource _retimed = new();
    private double _speed = 1;
    private readonly IClock _clock;
    private readonly IKeyOutput _output;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _running = new(true);
    private double? _pausedAt;
    private readonly TaskCompletionSource<PlaybackOutcome> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;

    /// <param name="octaveTimerMs">
    /// Turns on the live octave timer with this OctaveDelay (null = off, as in the original): see <see cref="OctaveTimer"/>.
    /// </param>
    public PlaybackLoop(IReadOnlyList<PlaybackEvent> events, IClock clock, IKeyOutput output, bool startPaused = false, double? octaveTimerMs = null)
    {
        _events = events;
        _keys = events.Select(e => e.Keys).ToArray();
        _octaveDelayMs = octaveTimerMs is > 0 ? octaveTimerMs : null;
        _due = events.Select(e => e.DueMs).ToArray();
        _clock = clock;
        _output = output;
        if (startPaused)
        {
            _running.Reset();
            _pausedAt = clock.NowMs;
        }
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
    public double? LastDueMs
    {
        get
        {
            lock (_timing)
            {
                var last = _due.Where(d => d is not null).DefaultIfEmpty().Max();
                // Paused: the rest of the song moves later for as long as the pause lasts.
                return last is { } l && _pausedAt is { } at && !CatchUp && l > at ? l + (_clock.NowMs - at) : last;
            }
        }
    }

    /// <summary>
    /// On resume, catch up the events missed during the pause instead of moving the rest of the song later (the
    /// original's behaviour; used by a host whose players keep playing through its pause).
    /// </summary>
    public bool CatchUp { get; set; }

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
        CancellationTokenSource? old = null;
        lock (_timing)
        {
            if (paused == IsPaused) return;
            var now = _clock.NowMs;
            if (paused) _pausedAt = now;
            else if (_pausedAt is { } at)
            {
                if (!CatchUp)
                    for (var i = Position; i < _due.Length; i++)
                        if (_due[i] is { } d) _due[i] = d + (now - at);
                _pausedAt = null;
                old = _retimed;
                _retimed = new CancellationTokenSource();
            }
            if (paused) _running.Reset(); else _running.Set();
        }
        old?.Cancel(); // wakes the loop so it waits for the new times
    }

    private void Run()
    {
        var outcome = PlaybackOutcome.Completed;
        try
        {
            var lastPress = double.NegativeInfinity;
            var timer = _octaveDelayMs is { } delay ? new OctaveTimer(_keys, delay, _clock.NowMs) : null;
            for (var i = 0; i < _events.Count; i++)
            {
                Position = i;
                double due;
                CancellationToken retimed;
                lock (_timing)
                {
                    due = _due[i] ?? double.NegativeInfinity;
                    retimed = _retimed.Token;
                }
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, retimed);

                // Live octave timer: while waiting for this event, press octave changes of coming chords early.
                while (timer?.Next(i, Math.Max(_clock.NowMs, lastPress + MinGapMs)) is { } early
                       && early.Time < (_keys[i].Length > 0 ? Math.Max(due, lastPress + MinGapMs) : due))
                {
                    if (!_clock.WaitUntil(early.Time, wait.Token) || !_running.IsSet || _cts.IsCancellationRequested) break;
                    var (window, key) = KeyMap.Decode(_keys[early.Index][early.Position])!.Value;
                    _keys[early.Index] = _keys[early.Index].Remove(early.Position, 1);
                    try { _output.Press(window, key); }
                    catch (Exception ex) { KeyError?.Invoke(ex); }
                    lastPress = _clock.NowMs;
                    timer.Swapped(window, lastPress);
                }

                if (_keys[i].Length > 0) due = Math.Max(due, lastPress + MinGapMs);
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
                    i--; // resumed: wait for this event again, at its new time
                    continue;
                }
                if (_cts.IsCancellationRequested) { outcome = PlaybackOutcome.Stopped; break; }
                var keys = _keys[i];
                if (keys.Length == 0) continue;
                try { _output.PressKeys(keys); }
                catch (Exception ex) { KeyError?.Invoke(ex); }
                lastPress = _clock.NowMs;
                timer?.Pressed(keys, lastPress);
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

/// <summary>
/// The live octave timer (test option, <see cref="OctaveSwapMode.LiveTimer"/>), one per game window:
/// <list type="number">
/// <item>Each time the window's octave changes (9 or 0, pressed early or with a chord) its timer starts, counting
/// in <see cref="PlaybackLoop.OctaveTimerStepMs"/> steps. Before the first change the timer counts as run out.</item>
/// <item>Once the timer has reached OctaveDelay, the window's next chord still to play is checked at every step
/// (filler, tempo elements and chords without keys for this window are skipped).</item>
/// <item>If that chord starts with an octave change, the change is pressed now, taken out of the chord, and the
/// timer starts again. A chord starting with two changes ("99") gets the second one OctaveDelay later, if there is
/// time before the chord.</item>
/// </list>
/// </summary>
internal sealed class OctaveTimer
{
    private readonly string[] _keys;
    private readonly double _delay;
    private readonly double[] _lastSwap = new double[KeyMap.WindowCount + 1];
    private readonly int[][] _next = new int[KeyMap.WindowCount + 1][];

    public OctaveTimer(string[] keys, double delayMs, double startMs)
    {
        _keys = keys;
        _delay = delayMs;
        // Run out at the start, with its steps counted from the start.
        Array.Fill(_lastSwap, startMs - Math.Ceiling(delayMs / PlaybackLoop.OctaveTimerStepMs) * PlaybackLoop.OctaveTimerStepMs);
        // _next[w][i]: the first element at or after i with a key for window w (keys.Length when none).
        for (var w = 1; w <= KeyMap.WindowCount; w++)
        {
            var next = new int[keys.Length + 1];
            next[keys.Length] = keys.Length;
            for (var i = keys.Length - 1; i >= 0; i--) next[i] = FirstKey(keys[i], w) >= 0 ? i : next[i + 1];
            _next[w] = next;
        }
    }

    /// <summary>
    /// The earliest early octave change from element <paramref name="from"/> on, no sooner than
    /// <paramref name="earliestMs"/>: when to press it, and which key character of which element it is.
    /// </summary>
    public (double Time, int Index, int Position)? Next(int from, double earliestMs)
    {
        (double Time, int Index, int Position)? best = null;
        for (var w = 1; w <= KeyMap.WindowCount; w++)
        {
            var j = _next[w][Math.Min(from, _keys.Length)];
            int pos;
            // Rare: an element can lose its only key for this window to an early change; look further then.
            while ((pos = j < _keys.Length ? FirstKey(_keys[j], w) : -1) < 0 && j < _keys.Length) j = _next[w][j + 1];
            if (pos < 0 || KeyMap.Decode(_keys[j][pos]) is not { Key: "9" or "0" }) continue;
            var start = _lastSwap[w];
            var at = Math.Max(start + _delay, earliestMs);
            var time = start + Math.Ceiling((at - start) / PlaybackLoop.OctaveTimerStepMs - 1e-9) * PlaybackLoop.OctaveTimerStepMs;
            if (best is null || time < best.Value.Time) best = (time, j, pos);
        }
        return best;
    }

    /// <summary>An octave change was pressed early for <paramref name="window"/>: its timer starts again.</summary>
    public void Swapped(int window, double atMs) => _lastSwap[window] = atMs;

    /// <summary>A chord was pressed: the timer of every window it changed the octave of starts again.</summary>
    public void Pressed(string keys, double atMs)
    {
        foreach (var c in keys)
            if (KeyMap.Decode(c) is { Key: "9" or "0" } k) _lastSwap[k.Window] = atMs;
    }

    private static int FirstKey(string keys, int window)
    {
        for (var p = 0; p < keys.Length; p++)
            if (KeyMap.Decode(keys[p])?.Window == window) return p;
        return -1;
    }
}
