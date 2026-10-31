using GW2MM.Core.Arrangement;

namespace GW2MM.Core.Playback;

/// <summary>Delivers one instrument key press (key down + key up) to the game window titled "Player&lt;window&gt;".</summary>
public interface IKeyOutput
{
    void Press(int window, string key);
}

public static class KeyOutputExtensions
{
    /// <summary>
    /// The original <c>PlayNotes</c>: presses each encoded key character of <paramref name="keys"/> in order
    /// (characters that are not key codes are skipped).
    /// </summary>
    public static void PressKeys(this IKeyOutput output, string keys)
    {
        if (output is OctaveTracker tracker)
        {
            foreach (var c in keys) tracker.PressKey(c);
            return;
        }
        foreach (var c in keys)
            if (KeyMap.Decode(c) is { } k)
                output.Press(k.Window, k.Key);
    }

    /// <summary>
    /// Returns the instruments to the middle octave after a song (finished or stopped) and on exit. With an
    /// <see cref="OctaveTracker"/> (what the sessions use) only the keys each window needs are pressed, after
    /// <paramref name="delayMs"/>; see <see cref="OctaveTracker.ResetAsync"/>. Otherwise the original
    /// <c>RefreshPlayers</c>: 9, 9, 0 to every window, waiting <paramref name="delayMs"/> before each round.
    /// </summary>
    public static async Task ResetOctavesAsync(this IKeyOutput output, int delayMs, CancellationToken ct = default)
    {
        if (output is OctaveTracker tracker)
        {
            await tracker.ResetAsync(delayMs, ct).ConfigureAwait(false);
            return;
        }
        foreach (var key in new[] { "9", "9", "0" })
        {
            await Task.Delay(Math.Max(0, delayMs), ct).ConfigureAwait(false);
            // Every window a part can play on (the original stopped at Player5, so Player6..8 kept their octave).
            for (var w = 1; w <= KeyMap.WindowCount; w++) output.Press(w, key);
        }
    }
}

/// <summary>
/// Follows each game window's octave (0 low, 1 middle, 2 high) through the songs played, so a reset only presses
/// what is needed to get back to the middle octave:
/// <list type="bullet">
/// <item>Every window is taken to be in the middle octave unless a tracked change says otherwise (at the start, and
/// after each reset).</item>
/// <item>Only the octave changes the arrangement added count (9 down, 0 up, kept within 0..2), whether pressed with
/// their chord or early. The 9 / 0 keys of MIDI notes 96 / 97 are pressed but not counted
/// (<see cref="KeyMap.ArrangedSwap"/>), nor are reset presses.</item>
/// </list>
/// One tracker is shared by everything that plays on this computer's windows.
/// </summary>
public sealed class OctaveTracker : IKeyOutput
{
    public const int Low = 0, Middle = 1, High = 2;

    private readonly IKeyOutput _inner;
    private readonly object _gate = new();
    private readonly int[] _octave = Enumerable.Repeat(Middle, KeyMap.WindowCount + 1).ToArray();

    public OctaveTracker(IKeyOutput inner) => _inner = inner;

    /// <summary><paramref name="keys"/> itself when it is a tracker already, so every user shares one.</summary>
    public static OctaveTracker Of(IKeyOutput keys) => keys as OctaveTracker ?? new OctaveTracker(keys);

    /// <summary>The tracked octave of window Player<paramref name="window"/>.</summary>
    public int OctaveOf(int window) { lock (_gate) return _octave[window]; }

    /// <summary>A press that is not tracked (as any key pressed by name).</summary>
    public void Press(int window, string key) => _inner.Press(window, key);

    /// <summary>Presses one encoded key character; an octave change the arrangement added moves the tracked octave.</summary>
    public void PressKey(char c)
    {
        if (KeyMap.Decode(c) is not { } k) return;
        _inner.Press(k.Window, k.Key);
        if (KeyMap.ArrangedSwap(c) is { } swap)
            lock (_gate) _octave[swap.Window] = Math.Clamp(_octave[swap.Window] + swap.Step, Low, High);
    }

    /// <summary>
    /// Waits <paramref name="delayMs"/> (so the game has applied the last octave change), then presses 9 in each
    /// window tracked in the high octave and 0 in each one in the low octave. Nothing is pressed (and there is no
    /// wait) when every window is in the middle octave already.
    /// </summary>
    public async Task ResetAsync(int delayMs, CancellationToken ct = default)
    {
        lock (_gate) if (_octave.All(o => o == Middle)) return;
        await Task.Delay(Math.Max(0, delayMs), ct).ConfigureAwait(false);
        List<(int Window, string Key)> presses;
        lock (_gate)
        {
            presses = Enumerable.Range(1, KeyMap.WindowCount).Where(w => _octave[w] != Middle)
                .Select(w => (w, _octave[w] == High ? "9" : "0")).ToList();
            for (var w = 1; w <= KeyMap.WindowCount; w++) _octave[w] = Middle;
        }
        foreach (var (w, key) in presses) _inner.Press(w, key);
    }
}
