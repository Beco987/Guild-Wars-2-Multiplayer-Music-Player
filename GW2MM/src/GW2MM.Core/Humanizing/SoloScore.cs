using GW2MM.Core.Arrangement;

namespace GW2MM.Core.Humanizing;

/// <summary>One press inside a chord: notes of one octave pressed together, or an octave change (9 / 0).</summary>
/// <param name="Keys">Encoded key characters (see <see cref="KeyMap"/>).</param>
/// <param name="TopPitch">MIDI pitch of the highest note (0 for an octave change).</param>
internal readonly record struct ChordStep(string Keys, bool IsSwap, int TopPitch);

/// <summary>What one game window plays at one moment of the score.</summary>
internal sealed class ScoreChord
{
    public required int Window { get; init; }
    public required double Tick { get; init; }
    /// <summary>Score time in ms, at the tempo the app plays the song (tempo map and tempo modifier).</summary>
    public required double Ms { get; init; }
    /// <summary>Octave changes needed before the chord, one key each, in order.</summary>
    public required IReadOnlyList<string> LeadingSwaps { get; init; }
    /// <summary>The chord's presses in playing order: starts and ends with notes, octave changes between note groups.</summary>
    public required IReadOnlyList<ChordStep> Steps { get; init; }

    /// <summary>The note group carrying the chord's highest pitch, normally the melody (the first such group on a tie).</summary>
    public int AnchorIndex
    {
        get
        {
            var best = -1;
            for (var i = 0; i < Steps.Count; i++)
                if (!Steps[i].IsSwap && (best < 0 || Steps[i].TopPitch > Steps[best].TopPitch)) best = i;
            return best;
        }
    }

    public int TopPitch => Steps.Where(s => !s.IsSwap).Max(s => s.TopPitch);
}

/// <summary>
/// The score of a solo performance: every window's chords with their octave changes. These are exactly the presses
/// the plain arrangement makes, so humanizing never adds or removes an octave change.
/// </summary>
internal sealed class SoloScore
{
    public required IReadOnlyList<ScoreChord> Chords { get; init; }
    /// <summary>Bars and beats, for the expressive options.</summary>
    public required Meter Meter { get; init; }
    /// <summary>Score ms of each played note's start and end (end null when unknown), for phrasing.</summary>
    public required IReadOnlyList<(double Start, double? End)> NoteSpans { get; init; }
    /// <summary>Score time in ms at a tick (tempo map and tempo modifier, as the app plays the song).</summary>
    public required Func<double, double> TickToMs { get; init; }

    public int WindowCount => Chords.Select(c => c.Window).Distinct().Count();

    /// <summary>Where the music ends (score ms): the last note's release, or the last onset when that is unknown.</summary>
    public double EndMs => Math.Max(Chords.Count > 0 ? Chords.Max(c => c.Ms) : 0,
        NoteSpans.Select(n => n.End ?? n.Start).DefaultIfEmpty(0).Max());

    private const int Down = 13, Up = 14; // KeyMap offsets of keys 9 and 0

    /// <summary>A one-player song (every track on one instrument, window 1), routed exactly as <see cref="SingleInstrument"/> does.</summary>
    public static SoloScore OnePlayer(CsvSong song, double? tempoFactor)
    {
        var toMs = SingleInstrument.TickToMs(song, tempoFactor);
        var chords = new List<ScoreChord>();
        foreach (var chord in SingleInstrument.Plan(song))
        {
            var octave = chord.Octave;
            var leading = new List<string>();
            var steps = new List<ChordStep>();
            foreach (var step in chord.Steps)
            {
                if (step.Swap != 0)
                {
                    octave += step.Swap;
                    var key = SingleInstrument.SwapKey(step.Swap);
                    if (steps.Count == 0) leading.Add(key);
                    else steps.Add(new ChordStep(key, true, 0));
                }
                else steps.Add(new ChordStep(SingleInstrument.NotesKey(step.Notes), false, SingleInstrument.PitchOf(octave, step.Notes.Max())));
            }
            if (steps.Count == 0) continue;
            chords.Add(new ScoreChord { Window = 1, Tick = chord.Tick, Ms = toMs(chord.Tick), LeadingSwaps = leading, Steps = steps });
        }
        return new SoloScore { Chords = chords, Meter = Meter.Of(song), NoteSpans = Spans(song, toMs, null), TickToMs = toMs };
    }

    /// <summary>
    /// A multi-track part (each track on its own window) from the arrangement, element by element. A window's octave
    /// changes that the arrangement pressed on their own, or together with or after its notes, become the leading
    /// changes of that window's next chord; changes between two note groups of one element stay inside the chord.
    /// </summary>
    /// <param name="tracks">The tracks the part plays (their notes' lengths are used for phrasing).</param>
    public static SoloScore FromPart(CsvSong song, SlotPlan part, double? tempoFactor, IReadOnlyCollection<int>? tracks = null)
    {
        var toMs = SingleInstrument.TickToMs(song, tempoFactor);
        var chords = new List<ScoreChord>();
        var octave = new Dictionary<int, int>();
        var pending = new Dictionary<int, List<string>>();
        for (var k = 0; k < part.Notes.Count; k++)
        {
            if (part.Ticks is null || k >= part.Ticks.Count || part.Ticks[k] is not { } tick) continue;
            var byWindow = new SortedDictionary<int, List<char>>();
            foreach (var c in part.Notes[k])
                if (KeyMap.Decode(c) is { } d)
                {
                    if (!byWindow.TryGetValue(d.Window, out var list)) byWindow[d.Window] = list = new List<char>();
                    list.Add(c);
                }
            foreach (var (w, keys) in byWindow)
            {
                var o = octave.GetValueOrDefault(w, 1);
                if (!pending.TryGetValue(w, out var swaps)) pending[w] = swaps = new List<string>();
                var steps = new List<ChordStep>();
                var group = new List<char>();
                var after = new List<string>(); // octave changes after the last note group so far
                void Flush()
                {
                    if (group.Count == 0) return;
                    foreach (var s in after) steps.Add(new ChordStep(s, true, 0));
                    after.Clear();
                    var top = group.Max(c => (c - 192) % 16);
                    steps.Add(new ChordStep(new string(group.ToArray()), false, 48 + 12 * Math.Clamp(o, 0, 2) + top));
                    group.Clear();
                }
                foreach (var c in keys)
                {
                    var offset = (c - 192) % 16;
                    if (offset is Down or Up)
                    {
                        Flush();
                        o += offset == Up ? 1 : -1;
                        (steps.Count == 0 ? swaps : after).Add(c.ToString());
                    }
                    else group.Add(c);
                }
                Flush();
                octave[w] = o;
                if (steps.Count == 0) continue;     // only octave changes: they lead into the window's next chord
                chords.Add(new ScoreChord { Window = w, Tick = tick, Ms = toMs(tick), LeadingSwaps = swaps.ToList(), Steps = steps });
                swaps.Clear();
                swaps.AddRange(after);
            }
        }
        return new SoloScore { Chords = chords, Meter = Meter.Of(song), NoteSpans = Spans(song, toMs, tracks), TickToMs = toMs };
    }

    private static List<(double, double?)> Spans(CsvSong song, Func<double, double> toMs, IReadOnlyCollection<int>? tracks) =>
        song.Notes.Where(n => tracks is null || tracks.Contains(n.Track))
            .Select(n => (toMs(n.Tick), n.EndTick is { } e && e > n.Tick ? toMs(e) : (double?)null))
            .ToList();
}
