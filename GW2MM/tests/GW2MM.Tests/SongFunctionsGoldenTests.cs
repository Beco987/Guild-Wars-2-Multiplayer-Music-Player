using GW2MM.Core.Arrangement;

namespace GW2MM.Tests;

/// <summary>Compares each ported list function with the original AutoHotkey implementation on ~4000 random vectors.</summary>
public class SongFunctionsGoldenTests
{
    public static IEnumerable<object[]> Vectors()
    {
        var inputs = Golden.ReadLines(Golden.Dir("functions", "vectors.in.txt"));
        var outputs = Golden.ReadLines(Golden.Dir("functions", "vectors.out.txt")).ToDictionary(l => l.Split('\t')[0]);
        foreach (var line in inputs)
        {
            var id = line.Split('\t')[0];
            yield return new object[] { line, outputs[id] };
        }
    }

    [Fact]
    public void AllVectorsMatchOriginal()
    {
        var failures = new List<string>();
        var counts = new Dictionary<string, int>();
        foreach (var v in Vectors())
        {
            var input = ((string)v[0]).Split('\t');
            var expected = ((string)v[1]).Split('\t').Skip(1).ToArray();
            var fn = input[1];
            counts[fn] = counts.GetValueOrDefault(fn) + 1;
            string[] actual = fn switch
            {
                "Combine" => Pair(SongFunctions.Combine(D(input[2]), D(input[3]), D(input[4]), D(input[5]))),
                "CombineFlags" => Pair(SongFunctions.CombineFlags(D(input[2]), D(input[3]), D(input[4]), D(input[5]))),
                "AddSwaps" => new[] { Golden.Encode(SongFunctions.AddSwaps(D(input[2]))) },
                "OpportunisticSwaps" => Pair(SongFunctions.OpportunisticSwaps(D(input[2]), D(input[3]), input[4])),
                "RemoveOutliers" => new[] { Golden.Encode(GW2MM.Core.Sessions.Statistics.RemoveOutliers(D(input[2]).Select(double.Parse).ToList())
                    .Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList()) },
                _ => throw new InvalidOperationException(fn),
            };
            if (!expected.SequenceEqual(actual))
                failures.Add($"{input[0]} {fn}\n  in:  {string.Join(" / ", input.Skip(2))}\n  exp: {string.Join(" / ", expected)}\n  got: {string.Join(" / ", actual)}");
        }
        Assert.True(counts.Values.All(c => c > 500), string.Join(",", counts));
        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n" + string.Join("\n", failures.Take(15)));
    }

    private static List<string> D(string s) => Golden.DecodeArray(s);
    private static string[] Pair(SongFunctions.Pair p) => new[] { Golden.Encode(p.Num), Golden.Encode(p.Alpha) };
}
