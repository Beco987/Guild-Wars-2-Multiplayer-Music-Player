using GW2MM.Core.Ahk;

namespace GW2MM.Core.Arrangement;

/// <summary>How octave changes are written into a part.</summary>
public enum OctaveSwapMode
{
    /// <summary>
    /// The default. Like <see cref="Original"/>, plus these fixes:
    /// <list type="bullet">
    /// <item>A jump straight between the low and high octave gets two octave changes. The original added none
    /// and silently dropped the notes.</item>
    /// <item>OctaveDelay is treated as milliseconds of real playback time: the song's tempo and the tempo modifier
    /// are taken into account. The original compared it with raw MIDI ticks.</item>
    /// <item>Each octave change goes as far ahead of its note as the gap allows. With room it gets at least
    /// OctaveDelay (the original's midpoint only guaranteed half of it). Without room it goes straight after the
    /// previous chord, instead of being pressed together with the note.</item>
    /// </list>
    /// </summary>
    Improved,

    /// <summary>Exactly the original scripts' <c>AddSwaps</c> / <c>OpportunisticSwaps</c> (kept for the oracle tests).</summary>
    Original,
}

/// <summary>
/// Octave handling for a part (see <see cref="OctaveSwapMode.Improved"/>). Guild Wars 2 instruments play one octave
/// at a time; "9" moves down an octave and "0" up. The game applies an octave change only after a round trip to
/// the server, so a note pressed too soon after it still sounds in the old octave. OctaveDelay is that time.
/// </summary>
public static class OctaveSwaps
{
    private const string LowRow = "zZxXcvVbBnNm";
    private const string HighRowNoC = "QwWerRtTyYui";

    /// <summary>
    /// <see cref="SongFunctions.AddSwaps"/> with the low &lt;-&gt; high jump fixed. A chord that needs the other
    /// outer octave gets two octave changes ("00" / "99") where the original added none and dropped the notes.
    /// Everything else is identical, chord by chord.
    /// </summary>
    public static List<string> AddSwaps(IReadOnlyList<string> chords)
    {
        var result = new List<string>(chords.Count);
        var octave = 1;
        foreach (var chord in chords)
        {
            var prefix = "";
            // A chord the original would mishandle: in the low octave with high-row notes (other than the top C of
            // the middle octave, "q"), or in the high octave with low-row notes. Go to the middle octave first, so
            // the original rules below add the second change.
            if (octave == 0 && chord.IndexOfAny(HighRowNoC.ToCharArray()) >= 0)
            {
                prefix = "0";
                octave = 1;
            }
            else if (octave == 2 && chord.IndexOfAny(LowRow.ToCharArray()) >= 0)
            {
                prefix = "9";
                octave = 1;
            }
            var single = SongFunctions.AddSwapsStep(chord, ref octave);
            result.Add(prefix + single);
        }
        return result;
    }

    /// <summary>
    /// Moves each chord's leading octave changes ("9", "0", "99", "00") into their own events before the chord.
    /// <para>The changes go between the previous chord (time p) and this one (time t). The last change aims for
    /// OctaveDelay before the note, and each earlier one OctaveDelay before the next. When there is plenty of room
    /// they stay where the original put a single change (evenly through the gap, i.e. the midpoint for one change).
    /// When there is not enough room they go as early as possible after the previous chord, so the game gets as much
    /// time as there is. A change is only pressed together with its note when the gap is too small to fit it.</para>
    /// <para>Times are MIDI ticks. <paramref name="msPerTick"/> converts at a given tick (song tempo times the
    /// tempo modifier, as playback does) so that OctaveDelay means milliseconds.</para>
    /// </summary>
    public static (List<string> Chords, List<string> Times) Place(IReadOnlyList<string> chords, IReadOnlyList<string> times,
        double octaveDelayMs, Func<double, double> msPerTick)
    {
        var outChords = new List<string>();
        var outTimes = new List<string>();
        for (var i = 0; i < chords.Count; i++)
        {
            var chord = chords[i];
            var time = i < times.Count ? times[i] : "";
            var swaps = 0;
            while (swaps < chord.Length && (chord[swaps] == '9' || chord[swaps] == '0') && chord[swaps] == chord[0]) swaps++;
            var t = AhkValue.Number(time);
            // The first chord has no earlier chord to make room after (its time is the start of the song).
            var p = i == 0 ? (double?)null : AhkValue.Number(i - 1 < times.Count ? times[i - 1] : "");
            if (swaps == 0 || swaps == chord.Length || t is null || p is null)
            {
                outChords.Add(chord);
                outTimes.Add(time);
                continue;
            }

            var gap = t.Value - p.Value;
            var delay = octaveDelayMs > 0 ? octaveDelayMs / Math.Max(msPerTick(t.Value), 1e-9) : 0;
            var placed = new double[swaps];
            var ok = true;
            for (var j = 1; j <= swaps; j++)
            {
                var even = p.Value + j * gap / (swaps + 1);          // the original's spot (midpoint for one change)
                var latestSafe = t.Value - (swaps - j + 1) * delay;   // OctaveDelay before the next press
                var earliest = j == 1 ? p.Value + 1 : placed[j - 2] + 1;
                var s = Math.Floor(Math.Max(earliest, Math.Min(even, latestSafe)));
                if (s < earliest) s = Math.Ceiling(earliest);
                if (s >= t.Value) { ok = false; break; }
                placed[j - 1] = s;
            }
            if (!ok)
            {
                outChords.Add(chord);
                outTimes.Add(time);
                continue;
            }
            for (var j = 0; j < swaps; j++)
            {
                outChords.Add(chord[0].ToString());
                outTimes.Add(AhkValue.Format((long)placed[j]));
            }
            outChords.Add(chord[swaps..]);
            outTimes.Add(time);
        }
        return (outChords, outTimes);
    }

    /// <summary>
    /// Milliseconds per MIDI tick at a given tick, the way playback converts (<c>DelayToClock</c>): tempo / 500000,
    /// times the song's tempo factor, using the last tempo change at or before that tick.
    /// </summary>
    public static Func<double, double> MsPerTick(CsvSong song, double? tempoFactor)
    {
        var changes = new List<(double Tick, double Tempo)>();
        for (var k = 0; k < song.TempoTimes.Count && k < song.TempoMarkers.Count; k++)
        {
            var marker = song.TempoMarkers[k];
            if (AhkValue.Number(song.TempoTimes[k]) is { } tick && marker.Length > 1 && AhkValue.Number(marker[1..]) is { } tempo && tempo > 0)
                changes.Add((tick, tempo));
        }
        changes.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        var factor = tempoFactor is { } f && f > 0 ? f : 1;
        return tick =>
        {
            var tempo = changes.Count > 0 ? changes[0].Tempo : 500000;
            foreach (var c in changes)
            {
                if (c.Tick > tick) break;
                tempo = c.Tempo;
            }
            return tempo / 500000 * factor;
        };
    }
}
