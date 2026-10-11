using System.Text;
using GW2MM.Core.Midi;

namespace GW2MM.Tests;

/// <summary>Byte-for-byte comparison of the built-in converter with the original Midicsv.exe on a generated corpus.</summary>
public class MidiCsvGoldenTests
{
    // Midicsv.exe reads past the end of these malformed chunks; its output there depends on uninitialised memory.
    private static readonly HashSet<string> Undefined = new() { "edge_no_eot", "edge_truncated", "edge_missing_track" };

    public static IEnumerable<object[]> Files() =>
        Directory.GetFiles(Golden.Dir("midi"), "*.mid").Select(f => new object[] { Path.GetFileNameWithoutExtension(f) }).OrderBy(o => (string)o[0]);

    [Theory]
    [MemberData(nameof(Files))]
    public void MatchesMidicsvExe(string name)
    {
        var mid = File.ReadAllBytes(Golden.Dir("midi", name + ".mid"));
        var result = MidiCsvConverter.Convert(mid, name + ".mid");
        if (Undefined.Contains(name))
        {
            Assert.NotNull(result.Csv);
            return;
        }
        var expected = File.ReadAllBytes(Golden.Dir("midi", name + ".csv"));
        var rc = int.Parse(File.ReadAllText(Golden.Dir("midi", name + ".rc")).Trim());
        Assert.Equal(rc != 0, result.Error is not null);
        Assert.Equal(Encoding.Latin1.GetString(expected), Encoding.Latin1.GetString(result.Csv));
    }
}
