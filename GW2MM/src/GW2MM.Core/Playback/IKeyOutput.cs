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
        foreach (var c in keys)
            if (KeyMap.Decode(c) is { } k)
                output.Press(k.Window, k.Key);
    }

    /// <summary>
    /// The original <c>RefreshPlayers</c>: returns the instruments in windows Player1..Player8 to the middle octave by
    /// pressing 9, 9, 0, waiting <paramref name="delayMs"/> before each round.
    /// </summary>
    public static async Task ResetOctavesAsync(this IKeyOutput output, int delayMs, CancellationToken ct = default)
    {
        foreach (var key in new[] { "9", "9", "0" })
        {
            await Task.Delay(Math.Max(0, delayMs), ct).ConfigureAwait(false);
            // Every window a part can play on (the original stopped at Player5, so Player6..8 kept their octave).
            for (var w = 1; w <= KeyMap.WindowCount; w++) output.Press(w, key);
        }
    }
}
