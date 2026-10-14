using GW2MM.Core.Arrangement;

namespace GW2MM.Core.Humanizing;

/// <summary>
/// The song's bars and beats from its time signatures (4/4 when it has none) and its ticks per quarter note. A new
/// time signature starts a new bar. Compound meters (6/8, 9/8, 12/8) count dotted-quarter beats.
/// </summary>
internal sealed class Meter
{
    private readonly List<(double Tick, double Bar, double Beat, int Beats)> _sections = new();

    public Meter(int division, IEnumerable<(double Tick, int Numerator, int Denominator)> signatures)
    {
        Division = division > 0 ? division : 480;
        foreach (var (tick, num, den) in signatures.OrderBy(s => s.Tick).Prepend((0, 4, 4)))
        {
            var bar = num * 4.0 * Division / den;
            var compound = den == 8 && num % 3 == 0 && num >= 6;
            var beat = compound ? 1.5 * Division : 4.0 * Division / den;
            var section = (Math.Max(0, tick), bar, beat, (int)Math.Round(bar / beat));
            if (_sections.Count > 0 && _sections[^1].Tick == section.Item1) _sections[^1] = section;
            else _sections.Add(section);
        }
    }

    public static Meter Of(CsvSong song) => new(song.Division, song.TimeSignatures);

    /// <summary>Ticks per quarter note.</summary>
    public int Division { get; }

    private (double Tick, double Bar, double Beat, int Beats) SectionAt(double tick)
    {
        var s = _sections[0];
        foreach (var x in _sections)
        {
            if (x.Tick > tick) break;
            s = x;
        }
        return s;
    }

    /// <summary>Length of a beat in ticks at a tick.</summary>
    public double BeatTicks(double tick) => SectionAt(tick).Beat;

    /// <summary>
    /// Metrical weight of an onset: 1 on the first beat of a bar, 0.75 on the middle of a bar of 4 (or 6, …) beats, 0.6
    /// on other beats, then 0.4, 0.25 and 0.15 for ever smaller subdivisions.
    /// </summary>
    public double Weight(double tick)
    {
        var (start, bar, beat, beats) = SectionAt(tick);
        var tolerance = Division / 32.0;
        var pos = (tick - start) % bar;
        bool On(double unit) { var r = pos % unit; return r <= tolerance || unit - r <= tolerance; }
        if (On(bar)) return 1;
        if (beats >= 4 && beats % 2 == 0 && On(bar / 2)) return 0.75;
        if (On(beat)) return 0.6;
        var sub = beat == 1.5 * Division ? beat / 3 : beat / 2;
        if (On(sub)) return 0.4;
        if (On(sub / 2)) return 0.25;
        return 0.15;
    }

    public bool IsDownbeat(double tick) => Weight(tick) >= 1;
}
