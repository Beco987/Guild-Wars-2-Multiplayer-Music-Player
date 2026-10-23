using System.Diagnostics;

namespace GW2MM.Core.Library;

/// <summary>
/// MuseScore scores (.mscz, .mscx) are imported by having MuseScore itself export a MIDI
/// ("MuseScore4.exe -o song.mid song.mscz"), which is then imported like any other MIDI. MuseScore must be installed.
/// </summary>
public static class MuseScore
{
    public const string ExeName = "MuseScore4.exe";

    /// <summary>How long one export may take before it is given up (MuseScore can be slow to start).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public static bool IsScore(string path) =>
        path.EndsWith(".mscz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mscx", StringComparison.OrdinalIgnoreCase);

    public static bool IsMidi(string path) =>
        path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".midi", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where MuseScore 4 is installed by default (all users, then the current user).</summary>
    public static IEnumerable<string> DefaultLocations()
    {
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                 })
        {
            if (folder.Length > 0) yield return Path.Combine(folder, "MuseScore 4", "bin", ExeName);
        }
    }

    /// <summary>The first of the saved path, then <paramref name="others"/>, that exists; null if none does.</summary>
    public static string? Find(string? saved, IEnumerable<string> others) =>
        new[] { saved }.Concat(others).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));

    public const string ExportFailed = "MuseScore could not export the score. Check that it opens in MuseScore 4.";

    /// <summary>
    /// Exports <paramref name="score"/> to the MIDI file <paramref name="midi"/>; throws IOException on failure. MuseScore's
    /// own output is not shown: it is mostly Qt logging, even when the export fails.
    /// </summary>
    public static async Task ExportMidiAsync(string exe, string score, string midi, CancellationToken ct = default)
    {
        File.Delete(midi);
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add(midi);
        start.ArgumentList.Add(score);

        using var process = Process.Start(start) ?? throw new IOException("MuseScore could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            ct.ThrowIfCancellationRequested();
            throw new IOException("MuseScore took too long to export the score.");
        }
        if (process.ExitCode != 0 || !File.Exists(midi) || new FileInfo(midi).Length == 0)
            throw new IOException(ExportFailed);
    }
}
