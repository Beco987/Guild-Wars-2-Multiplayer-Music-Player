using System.Text;

namespace GW2MM.Core.Text;

/// <summary>
/// File text handling compatible with the original AutoHotkey v1 scripts, which read and wrote Data.csv,
/// Config.txt and the song CSVs in the system ANSI code page unless the file carries a BOM.
/// </summary>
public static class LegacyText
{
    static LegacyText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The ANSI code page AutoHotkey uses for files without a BOM (Windows-1252 when it cannot be determined).</summary>
    public static Encoding Ansi
    {
        get
        {
            var cp = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            try { return Encoding.GetEncoding(cp is 0 or 65001 ? 1252 : cp); }
            catch (ArgumentException) { return Encoding.GetEncoding(1252); }
            catch (NotSupportedException) { return Encoding.GetEncoding(1252); }
        }
    }

    public readonly record struct FileText(string Text, Encoding Encoding);

    public static FileText Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new FileText(new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new FileText(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        var ansi = Ansi;
        return new FileText(ansi.GetString(bytes), ansi);
    }

    public static string ReadText(string path) => Read(path).Text;

    /// <summary>Writes text without changing the file's existing encoding (ANSI for new files).</summary>
    public static void Write(string path, string text, Encoding? encoding = null)
    {
        encoding ??= File.Exists(path) ? Read(path).Encoding : Ansi;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray());
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Appends like AutoHotkey's FileAppend (creating the file in ANSI if needed).</summary>
    public static void Append(string path, string text)
    {
        var existing = File.Exists(path) ? Read(path) : new FileText("", Ansi);
        Write(path, existing.Text + text, existing.Encoding);
    }

    /// <summary>
    /// Splits text into lines the way AutoHotkey's <c>Loop, Read</c> does: on LF, dropping a CR before it,
    /// with no extra empty line after a final line break.
    /// </summary>
    public static List<string> Lines(string text)
    {
        var lines = text.Split('\n').Select(l => l.EndsWith('\r') ? l[..^1] : l).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }
}
