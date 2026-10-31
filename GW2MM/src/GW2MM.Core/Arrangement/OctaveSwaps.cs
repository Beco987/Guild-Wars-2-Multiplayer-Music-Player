using GW2MM.Core.Ahk;

namespace GW2MM.Core.Arrangement;

/// <summary>How octave changes are written into a part.</summary>
public enum OctaveSwapMode
{
    /// <summary>
    /// Not used by the player (kept for comparison in the tests). Like <see cref="Original"/>, plus these changes:
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

    /// <summary>
    /// Exactly the original scripts' <c>AddSwaps</c> / <c>OpportunisticSwaps</c>. A chord that starts with an octave
    /// change gets the change as its own event at the midpoint of the gap since the previous chord when that gap (in
    /// MIDI ticks) is at least OctaveDelay; otherwise the change is pressed with the chord. A jump straight between the
    /// low and high octave gets no change at all, and its notes are dropped. The oracle tests check it against the
    /// AutoHotkey scripts byte for byte; the player uses <see cref="Player"/>.
    /// </summary>
    Original,

    /// <summary>
    /// What the player uses: <see cref="Original"/>, except that a jump straight between the low and high octave
    /// (high E to low E, say) gets two octave changes ("99" / "00") instead of dropping the notes. The two changes go
    /// into the gap before the note, OctaveDelay apart where the gap allows, and no note is ever moved
    /// (<see cref="OctaveSwaps.PlaceForPlayer"/>). Every other chord is the original's, change for change.
    /// </summary>
    Player,

    /// <summary>
    /// Test option (Settings → Live octave timer): the original <c>AddSwaps</c> (with the two changes of
    /// <see cref="Player"/> for a jump between the low and high octave) without <c>OpportunisticSwaps</c>.
    /// Every octave change stays at the start of its chord; playback then presses it early with a live timer
    /// (see <see cref="Playback.PlaybackLoop"/>).
    /// </summary>
    LiveTimer,
}

/// <summary>
/// Octave handling beyond the original's: <see cref="AddSwaps"/> and <see cref="PlaceForPlayer"/> for the player
/// (<see cref="OctaveSwapMode.Player"/>), and <see cref="Place"/> for the test-only <see cref="OctaveSwapMode.Improved"/>. Guild Wars 2 instruments play one octave
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
    /// The player's placement (<see cref="OctaveSwapMode.Player"/>), for chords from <see cref="AddSwaps"/>:
    /// <list type="bullet">
    /// <item>A chord that starts with two octave changes (a jump between the low and high octave) gets both as their
    /// own events in the gap since the previous chord (<see cref="PlaceTwo"/>): OctaveDelay apart and OctaveDelay
    /// before the note where the gap allows, sharing the gap evenly where it does not. Notes never move.</item>
    /// <item>Every other chord is placed exactly as the original <see cref="SongFunctions.OpportunisticSwaps"/>.</item>
    /// </list>
    /// OctaveDelay is milliseconds here (150 when blank), converted with <paramref name="msPerTick"/>; the original's
    /// rule for one change keeps comparing it with MIDI ticks. Returns (Num = chords, Alpha = times) like
    /// OpportunisticSwaps; <paramref name="origin"/> receives each output element's chord and start position in it.
    /// </summary>
    public static SongFunctions.Pair PlaceForPlayer(IReadOnlyList<string> chords, IReadOnlyList<string> times, string octaveDelay,
        Func<double, double> msPerTick, List<(int Chord, int Start)>? origin = null)
    {
        var delayMs = SingleInstrument.DelayMs(octaveDelay);
        var outChords = new List<string>();
        var outTimes = new List<string>();
        void Add(string chord, string time, int idx, int start)
        {
            outChords.Add(chord);
            outTimes.Add(time);
            origin?.Add((idx, start));
        }
        double? lastSwap = null; // when the latest octave change is pressed (MIDI ticks)
        for (var idx = 0; idx < chords.Count; idx++)
        {
            var chord = chords[idx];
            var time = idx < times.Count ? times[idx] : "";
            var t = AhkValue.Number(time);
            if (LeadingChanges(chord) == 2)
            {
                var p = idx == 0 ? null : AhkValue.Number(idx - 1 < times.Count ? times[idx - 1] : "");
                if (p is { } prev && t is { } now && PlaceTwo(prev, now, lastSwap, delayMs / Math.Max(msPerTick(now), 1e-9)) is { } at)
                {
                    Add(chord[0].ToString(), AhkValue.Format(at.First), idx, 0);
                    Add(chord[1].ToString(), AhkValue.Format(at.Second), idx, 1);
                    Add(chord[2..], time, idx, 2);
                    lastSwap = HasChange(chord[2..]) ? now : at.Second;
                }
                else
                {
                    Add(chord, time, idx, 0); // no room for two separate presses: both with the note
                    lastSwap = t ?? lastSwap;
                }
                continue;
            }
            if (SongFunctions.OpportunisticTime(chords, times, idx, octaveDelay) is { } mid)
            {
                Add(chord[0].ToString(), mid, idx, 0);
                Add(chord[1..], time, idx, 1);
                if (KeyIsChange(chord, 0)) lastSwap = AhkValue.Number(mid);
                if (HasChange(chord[1..])) lastSwap = t ?? lastSwap;
                continue;
            }
            Add(chord, time, idx, 0);
            if (HasChange(chord)) lastSwap = t ?? lastSwap;
        }
        return new SongFunctions.Pair(outChords, outTimes);
    }

    /// <summary>
    /// Where the two octave changes before a note go, as MIDI ticks: the note is at <paramref name="t"/>, the previous
    /// chord at <paramref name="p"/>, the last octave change (if any) at <paramref name="lastSwap"/>, and OctaveDelay is
    /// <paramref name="d"/> ticks. Notes never move, so the changes always fall strictly between the two chords.
    /// <list type="number">
    /// <item>Room for OctaveDelay three times over: at one and two thirds of the gap (evenly through it, as the
    /// original puts one change at its middle).</item>
    /// <item>Room for OctaveDelay between the changes, before the note and since the last change: OctaveDelay and
    /// twice OctaveDelay before the note.</item>
    /// <item>Less room: the time from the last change (or from the previous chord) to the note is shared evenly,
    /// so the changes are as far apart, and as far ahead of the note, as the song allows.</item>
    /// </list>
    /// Null when there is no room for two separate presses (a gap under 3 ticks).
    /// </summary>
    public static (long First, long Second)? PlaceTwo(double p, double t, double? lastSwap, double d)
    {
        var gap = t - p;
        double s1, s2;
        if (gap >= 3 * d)
        {
            s1 = p + gap / 3;
            s2 = p + 2 * gap / 3;
        }
        else
        {
            var fromLast = lastSwap is { } l ? (t - l) / 3 : double.PositiveInfinity; // three equal steps: last change, first, second, note
            var fromPrev = (t - p - 1) / 2;                                          // two equal steps after the previous chord
            var step = Math.Min(fromLast, fromPrev);
            if (step >= d) { s1 = t - 2 * d; s2 = t - d; }
            else if (fromLast <= fromPrev) { s1 = lastSwap!.Value + step; s2 = lastSwap.Value + 2 * step; }
            else { s1 = p + 1; s2 = p + 1 + step; }
        }
        var first = (long)Math.Floor(s1);
        var second = (long)Math.Floor(s2);
        return first > p && second > first && second < t ? (first, second) : null;
    }

    /// <summary>How many octave changes in the same direction a chord starts with (at most 2 from <see cref="AddSwaps"/>).</summary>
    private static int LeadingChanges(string chord)
    {
        var n = 0;
        while (n < chord.Length && chord[n] is '9' or '0' && chord[n] == chord[0] && n < SongArranger.NoteSwapsIn(chord)) n++;
        return n;
    }

    /// <summary>Whether the key at <paramref name="i"/> is an octave change the arrangement added (not MIDI note 96 / 97).</summary>
    private static bool KeyIsChange(string chord, int i) => chord[i] is '9' or '0' && i < SongArranger.NoteSwapsIn(chord);

    private static bool HasChange(string chord)
    {
        for (var i = 0; i < chord.Length; i++)
            if (KeyIsChange(chord, i)) return true;
        return false;
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
        changes = changes.Select((c, k) => (c, k)).OrderBy(x => x.c.Tick).ThenBy(x => x.k).Select(x => x.c).ToList(); // stable
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
