using System.Collections.Concurrent;
using GW2MM.Core.Playback;
using GW2MM.Core.Sessions;

namespace GW2MM.Tests;

/// <summary>Deterministic clock: waiting jumps straight to the deadline.</summary>
internal sealed class VirtualClock : IClock
{
    private readonly object _gate = new();
    private double _now;
    public VirtualClock(double start = 0) => _now = start;
    public double NowMs { get { lock (_gate) return _now; } }
    public void Advance(double ms) { lock (_gate) _now += ms; }
    public bool WaitUntil(double dueMs, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        lock (_gate) if (dueMs > _now) _now = dueMs;
        return !ct.IsCancellationRequested;
    }
}

/// <summary>A real clock shifted by a fixed offset, to give each simulated machine its own time base.</summary>
internal sealed class OffsetClock : IClock
{
    private readonly double _offset;
    public OffsetClock(double offset) => _offset = offset;
    public double NowMs => PrecisionClock.Instance.NowMs + _offset;
    public bool WaitUntil(double dueMs, CancellationToken ct) => PrecisionClock.Instance.WaitUntil(dueMs - _offset, ct);
    /// <summary>Converts this machine's time to the shared real time base.</summary>
    public double ToReal(double t) => t - _offset;
}

internal sealed record Press(double Time, int Window, string Key);

internal sealed class RecordingKeys : IKeyOutput
{
    private readonly IClock _clock;
    public readonly ConcurrentQueue<Press> Presses = new();
    public RecordingKeys(IClock clock) => _clock = clock;
    public void Press(int window, string key) => Presses.Enqueue(new Press(_clock.NowMs, window, key));
    public List<Press> All => Presses.ToList();
}

internal sealed class FakeMeter : IInputDelayMeter
{
    private readonly IReadOnlyList<double> _samples;
    public FakeMeter(params double[] samples) => _samples = samples;
    public Task<IReadOnlyList<double>> MeasureAsync(CancellationToken ct) => Task.FromResult(_samples);
}

/// <summary>A meter whose test cannot run (Guild Wars 2 is not open).</summary>
internal sealed class GameClosedMeter : IInputDelayMeter
{
    public Task<IReadOnlyList<double>> MeasureAsync(CancellationToken ct) =>
        Task.FromException<IReadOnlyList<double>>(new InputDelayException("Guild Wars 2 is not open. Start the game, then run Sync again."));
}

internal class FakeView : IHostView
{
    public readonly ConcurrentQueue<string> Dialog = new();
    public readonly ConcurrentQueue<string> Events = new();
    public readonly ConcurrentQueue<string> Statuses = new();
    public readonly ConcurrentQueue<string> Messages = new();
    public readonly ConcurrentQueue<string> Problems = new();
    public readonly ConcurrentDictionary<int, string> InputDelays = new();
    public readonly ConcurrentDictionary<int, string> ClientPings = new();
    public volatile bool ChatEnabled;
    public int Disconnects, ShownMainWindow;

    public void SetStatus(string text) => Statuses.Enqueue(text);
    public void AppendDialog(string line) => Dialog.Enqueue(line);
    public void LogEvent(string line) => Events.Enqueue(line);
    public void SetChatEnabled(bool enabled) => ChatEnabled = enabled;
    public void ShowMessage(string text) => Messages.Enqueue(text);
    public Task ShowMessageAndWaitAsync(string text) { Messages.Enqueue(text); return Task.CompletedTask; }
    public void ReportProblem(string text) => Problems.Enqueue(text);
    public void SetInputDelay(int slot, string value) => InputDelays[slot] = value;
    public void SetClientPing(int slot, string value) => ClientPings[slot] = value;
    public void OnClientDisconnected() => Interlocked.Increment(ref Disconnects);
    public void ShowMainWindow() => Interlocked.Increment(ref ShownMainWindow);
    public int PlayerChanges;
    public void OnPlayersChanged() => Interlocked.Increment(ref PlayerChanges);
    public string LastStatus => Statuses.LastOrDefault() ?? "";
}

internal static class Wait
{
    public static async Task Until(Func<bool> condition, int timeoutMs = 5000, string? what = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Timed out waiting for " + (what ?? "condition"));
            await Task.Delay(10);
        }
    }
}
