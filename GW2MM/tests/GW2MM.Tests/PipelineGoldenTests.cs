using System.Globalization;
using GW2MM.Core.Arrangement;
using GW2MM.Core.Library;
using GW2MM.Core.Playback;
using GW2MM.Core.Text;

namespace GW2MM.Tests;

/// <summary>
/// End-to-end comparison with the original host pipeline (SongPlayer -> TrackWriter -> AddFiller -> CombineFlags ->
/// DelayToClock -> TrackSharer) run under AutoHotkey: per-slot key strings, delays, absolute times, latencies and
/// which clients are sent a part.
/// </summary>
public class PipelineGoldenTests
{
    public static IEnumerable<object[]> Cases() =>
        Directory.GetFiles(Golden.Dir("pipeline"), "*.case.txt").Select(f => new object[] { Path.GetFileName(f)[..^".case.txt".Length] }).OrderBy(o => (string)o[0]);

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesOriginal(string name)
    {
        var spec = Golden.ReadLines(Golden.Dir("pipeline", name + ".case.txt")).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var outLines = Golden.ReadLines(Golden.Dir("pipeline", name + ".out.txt"));
        var expected = outLines.Where(l => l.Contains('=') && !l.StartsWith("SEND")).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var sends = outLines.Where(l => l.StartsWith("SEND")).Select(l => l.Split('\t')).ToList();

        var songPath = Golden.Dir("songs", spec["song"] + ".csv");
        var lines = File.Exists(songPath) ? LegacyText.Lines(LegacyText.ReadText(songPath)) : new List<string>();
        double? tempo = spec.ContainsKey("fromclient")
            ? GW2MM.Core.Ahk.AhkValue.Number(spec["clienttempo"])
            : SongLibrary.TempoFactorFromFields(new[] { "Some Other Song", " 2", " 3", " desc", spec["song"], " 3", " " + spec["tempomod"], " generated" }, spec["song"]);

        var plan = SongArranger.Arrange(CsvSong.Parse(lines), PortMapping.Parse(spec["ip"]), spec["octavedelay"], tempo);

        var gw2 = spec["gw2"].Split(',').Select(double.Parse).ToArray();
        var ticks = spec["ticks"].Split(',').Select(double.Parse).ToArray();
        ticks[0] = double.Parse(expected["tick1"], CultureInfo.InvariantCulture);
        var sentSlots = new List<int>();
        for (var slot = 1; slot <= 5; slot++)
        {
            var p = plan[slot];
            Assert.Equal(Golden.DecodeNotes(expected["notes" + slot]), p.Notes);
            // A slot with 1-3 keyed elements keeps unused raw (unscaled) gaps in the original's DelayToPlay; it is never
            // scheduled, so only the delays of slots that reach DelayToClock are compared.
            if (p.HasKeys)
                Assert.Equal(Golden.DecodeArray(expected["delays" + slot]), p.DelaysMs.Select(d => d.ToString(CultureInfo.InvariantCulture)).ToList());
            var latency = HostLatency(gw2, slot);
            if (p.HasKeys)
                Assert.Equal(double.Parse(expected["latency" + slot], CultureInfo.InvariantCulture), latency);
            var times = p.IsActive ? Schedule.AbsoluteTimes(p, ticks[slot - 1], latency) : new List<double>();
            Assert.Equal(Golden.DecodeArray(expected["times" + slot]), times.Select(t => t.ToString(CultureInfo.InvariantCulture)).ToList());
            if (slot != 1 && p.IsActive) sentSlots.Add(slot);
        }
        var expectedSlots = sends.Where(s => s[2].StartsWith("Note_1")).Select(s => int.Parse(s[1]) - 99).ToList();
        Assert.Equal(expectedSlots, sentSlots);
    }

    // latency = client's input delay minus the host's (the original's Ip<n>GW2 - Pings[1], with Pings[1] = host delay after Sync).
    private static double HostLatency(double[] gw2, int slot) => gw2[slot - 1] - gw2[0];
}
