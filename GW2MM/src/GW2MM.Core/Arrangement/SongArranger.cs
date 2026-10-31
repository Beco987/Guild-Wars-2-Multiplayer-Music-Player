using GW2MM.Core.Ahk;

namespace GW2MM.Core.Arrangement;

/// <summary>The keys and gaps one player slot will play, before it is anchored to a clock.</summary>
/// <param name="Slot">Player slot 1..5.</param>
/// <param name="Notes">Key string per element (<c>NewNotes</c> in the original); "" for filler and tempo elements.</param>
/// <param name="DelaysMs">Gap in ms from each element to the next (<c>DelayToPlay</c>); the trailing gaps are 0.</param>
/// <param name="HasKeys">More than 3 keyed elements (the original's <c>KeysToPlay.Length() &gt; 3</c>); decides whether latency applies.</param>
public sealed record SlotPlan(int Slot, IReadOnlyList<string> Notes, IReadOnlyList<long> DelaysMs, bool HasKeys)
{
    /// <summary>The original only schedules/sends a part with more than 3 elements.</summary>
    public bool IsActive => Notes.Count > 3;
}

public sealed record SongPlan(IReadOnlyList<SlotPlan> Slots)
{
    public SlotPlan this[int slot] => Slots[slot - 1];
}

/// <summary>
/// Turns a parsed song into per-slot plans; a port of the original <c>SongPlayer</c> label from
/// <c>TrackWriter</c> through <c>AddFiller</c>, <c>CombineFlags</c> and <c>DelayToClock</c>.
/// </summary>
public static class SongArranger
{
    public static SongPlan Arrange(CsvSong song, PortMapping mapping, string octaveDelay, double? tempoFactor,
        OctaveSwapMode octaveSwaps = OctaveSwapMode.Player, bool singleInstrument = false)
    {
        if (singleInstrument)
        {
            // One-player song: every track on one instrument, played by each slot that has a track ticked.
            var players = SingleInstrument.Players(mapping);
            var part = SingleInstrument.Arrange(song, 1, octaveDelay, tempoFactor);
            return new SongPlan(Enumerable.Range(1, PortMapping.Slots)
                .Select(s => players.Contains(s) ? part with { Slot = s } : new SlotPlan(s, Array.Empty<string>(), Array.Empty<long>(), false))
                .ToList());
        }
        var times = new List<string>[PortMapping.Slots + 1];
        var keys = new List<string>[PortMapping.Slots + 1];
        var msPerTick = OctaveSwaps.MsPerTick(song, tempoFactor);
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
            (times[slot], keys[slot]) = TrackWriter(song, mapping, slot, octaveDelay, octaveSwaps, msPerTick);

        AddFiller(times, keys);

        var delays = new List<double?>[PortMapping.Slots + 1];
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
        {
            delays[slot] = new List<double?>();
            if (keys[slot].Count == 0) continue;
            var r = SongFunctions.CombineFlags(times[slot], keys[slot], song.TempoTimes, song.TempoMarkers);
            times[slot] = r.Num;
            keys[slot] = r.Alpha;
            for (var k = 0; k < times[slot].Count; k++)
            {
                var next = k + 1 < times[slot].Count ? times[slot][k + 1] : "";
                var d = SongFunctions.Sub(next, times[slot][k]);
                delays[slot].Add(d is null ? null : d.Value / 500000);
            }
        }

        // DelayToClock: note that the running tempo deliberately carries over from one slot to the next.
        double? tempo = 1;
        var plans = new List<SlotPlan>();
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
        {
            var hasKeys = keys[slot].Count > 3;
            var notes = new List<string>();
            var ms = new List<long>();
            if (hasKeys)
            {
                for (var k = 0; k < delays[slot].Count; k++)
                {
                    var item = k < keys[slot].Count ? keys[slot][k] : "";
                    var isTempo = item.Length > 0 && item[0] == 'B';
                    if (isTempo) tempo = AhkValue.Number(item[1..]);
                    var d = delays[slot][k];
                    double? product = d is null || tempo is null || tempoFactor is null ? null : d.Value * tempo.Value * tempoFactor.Value;
                    ms.Add(AhkValue.Round(AhkValue.Floor(product)));
                    notes.Add(isTempo ? "" : item);
                }
            }
            plans.Add(new SlotPlan(slot, notes, ms, hasKeys));
        }
        return new SongPlan(plans);
    }

    /// <summary>
    /// <c>TrackWriter(Index)</c>: for each mapped track, merge simultaneous notes, add octave swaps, move swaps
    /// into gaps, encode the keys for the output window, then merge all tracks of the slot.
    /// The host (slot 1) sends track <i>n</i> to window Player<i>n</i>; a client sends its k-th mapped track
    /// to Player<i>k</i>.
    /// </summary>
    internal static (List<string> Times, List<string> Keys) TrackWriter(CsvSong song, PortMapping mapping, int slot, string octaveDelay,
        OctaveSwapMode octaveSwaps = OctaveSwapMode.Player, Func<double, double>? msPerTick = null)
    {
        var outTimes = Enumerable.Range(0, PortMapping.Tracks + 1).Select(_ => new List<string>()).ToArray();
        var outKeys = Enumerable.Range(0, PortMapping.Tracks + 1).Select(_ => new List<string>()).ToArray();
        var empty = new List<string>();
        var window = 1;
        for (var track = 1; track <= PortMapping.Tracks; track++)
        {
            if (mapping[slot, track])
            {
                var c = SongFunctions.Combine(song.TrackTimes[track], song.TrackNotes[track], empty, empty);
                var tdur = c.Num;
                if (octaveSwaps is OctaveSwapMode.Original or OctaveSwapMode.Player)
                {
                    var original = octaveSwaps == OctaveSwapMode.Original;
                    var tindex = original ? SongFunctions.AddSwaps(c.Alpha) : OctaveSwaps.AddSwaps(c.Alpha);
                    var origin = new List<(int Chord, int Start)>();
                    var o = original
                        ? SongFunctions.OpportunisticSwaps(tindex, tdur, octaveDelay, origin)
                        : OctaveSwaps.PlaceForPlayer(tindex, tdur, octaveDelay, msPerTick ?? (_ => 1), origin);
                    outTimes[window] = o.Alpha;
                    outKeys[window] = Encode(o.Num.Select((n, k) => (n, NoteSwapsIn(tindex[origin[k].Chord]) - origin[k].Start)).ToList(), window);
                }
                else if (octaveSwaps == OctaveSwapMode.LiveTimer)
                {
                    // The changes stay in their chords; the playback loop's live timer presses them early.
                    outTimes[window] = c.Num;
                    outKeys[window] = Encode(OctaveSwaps.AddSwaps(c.Alpha).Select(n => (n, NoteSwapsIn(n))).ToList(), window);
                }
                else
                {
                    var chords = OctaveSwaps.AddSwaps(c.Alpha);
                    var (placed, placedTimes) = OctaveSwaps.Place(chords, tdur, AhkValue.Number(octaveDelay) ?? 0, msPerTick ?? (_ => 1));
                    outTimes[window] = placedTimes;
                    outKeys[window] = SongFunctions.ReplaceChars(placed, KeyMap.Replacements(window));
                }
                if (slot != 1) window++;
            }
            if (slot == 1) window++;
        }

        var merged = SongFunctions.Combine(outTimes[1], outKeys[1], outTimes[2], outKeys[2]);
        for (var w = 3; w <= PortMapping.Tracks; w++)
            merged = SongFunctions.Combine(merged.Num, merged.Alpha, outTimes[w], outKeys[w]);
        return (merged.Num, merged.Alpha);
    }

    /// <summary>
    /// Where the 9 / 0 keys of MIDI notes 96 / 97 start in a chord from <see cref="SongFunctions.AddSwaps"/>: they are
    /// its trailing run of 9s and 0s (AddSwaps writes them last, and every octave change it adds is followed by a note).
    /// Returns the chord's length when there are none.
    /// </summary>
    internal static int NoteSwapsIn(string chord)
    {
        var start = chord.Length;
        while (start > 0 && chord[start - 1] is '9' or '0') start--;
        return start;
    }

    /// <summary>
    /// <see cref="SongFunctions.ReplaceChars"/> for one window, except that the 9 / 0 keys from <c>from</c> on (MIDI
    /// notes 96 / 97) get their own characters (<see cref="KeyMap.NoteSwap"/>) so the octave tracker leaves them out.
    /// </summary>
    private static List<string> Encode(List<(string Chord, int NoteSwapsFrom)> chords, int window)
    {
        var replacements = KeyMap.Replacements(window);
        return chords.Select(x =>
        {
            var from = Math.Max(0, x.NoteSwapsFrom);
            var head = SongFunctions.ReplaceChars(new[] { x.Chord[..Math.Min(from, x.Chord.Length)] }, replacements)[0];
            var tail = from < x.Chord.Length ? string.Concat(x.Chord[from..].Select(ch => KeyMap.NoteSwap(window, ch == '0'))) : "";
            return head + tail;
        }).ToList();
    }

    /// <summary>
    /// <c>AddFiller</c>: gives every active slot an element (blank if it plays nothing there) at every time any
    /// active slot plays, so all parts advance through the same timeline. Slot 1 always receives the filler.
    /// </summary>
    private static void AddFiller(List<string>[] times, List<string>[] keys)
    {
        var fillerTimes = new List<string>();
        var fillerKeys = new List<string>();
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
        {
            if (keys[slot].Count <= 3) continue;
            var f = SongFunctions.Combine(fillerTimes, fillerKeys, times[slot], keys[slot]);
            fillerTimes = f.Num;
            fillerKeys = f.Alpha;
        }
        var blanks = fillerKeys.Select(_ => "").ToList();
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
        {
            if (slot != 1 && keys[slot].Count <= 3) continue;
            var f = SongFunctions.Combine(times[slot], keys[slot], fillerTimes, blanks);
            times[slot] = f.Num;
            keys[slot] = f.Alpha;
        }
    }
}
