using System.Text;

namespace GW2MM.Core.Midi;

/// <summary>
/// Converts a Standard MIDI File to the CSV format of John Walker's midicsv 1.1 (the Midicsv.exe the original
/// application shipped and ran through a temporary .bat file). Output is byte-for-byte identical to Midicsv.exe for
/// well-formed files and for the errors it reports deterministically (verified against Midicsv.exe in the golden
/// tests): records are "Track, Time, Type, values" lines terminated by CRLF, text is quoted with '"' doubled,
/// '\' doubled and bytes 0x00-0x1F/0x7F-0xA0 written as \ooo octal escapes.
/// <para>
/// Where Midicsv.exe reads past the end of a malformed track (no End_track event, truncated chunk) its output
/// depends on uninitialised memory; this converter instead stops at the end of the chunk.
/// </para>
/// </summary>
public static class MidiCsvConverter
{
    public sealed record Result(byte[] Csv, string? Error);

    public static Result ConvertFile(string midiPath, string csvPath)
    {
        var result = Convert(File.ReadAllBytes(midiPath), Path.GetFileName(midiPath));
        var dir = Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(csvPath, result.Csv);
        return result;
    }

    public static Result Convert(byte[] data, string fileName)
    {
        var w = new Writer();
        if (data.Length < 14 || data[0] != 'M' || data[1] != 'T' || data[2] != 'h' || data[3] != 'd')
            return new Result(w.ToArray(), $"{fileName} is not a Standard MIDI File.");

        var format = (short)((data[8] << 8) | data[9]);
        var nTracks = (short)((data[10] << 8) | data[11]);
        var division = (short)((data[12] << 8) | data[13]);
        w.Line($"0, 0, Header, {format}, {nTracks}, {division}");

        var pos = 14; // midicsv ignores the declared header length
        for (var track = 1; track <= nTracks; track++)
        {
            if (pos + 8 > data.Length || data[pos] != 'M' || data[pos + 1] != 'T' || data[pos + 2] != 'r' || data[pos + 3] != 'k')
                return new Result(w.ToArray(), $"Track {track} header is invalid.");
            var length = (int)Math.Min(((uint)data[pos + 4] << 24) | ((uint)data[pos + 5] << 16) | ((uint)data[pos + 6] << 8) | data[pos + 7], int.MaxValue);
            pos += 8;
            var available = Math.Max(0, Math.Min(length, data.Length - pos));
            var chunk = new byte[length];
            Array.Copy(data, pos, chunk, 0, available);
            pos += available;
            w.Line($"{track}, 0, Start_track");
            ConvertTrack(w, track, chunk);
        }
        w.Line("0, 0, End_of_file");
        return new Result(w.ToArray(), null);
    }

    private static void ConvertTrack(Writer w, int track, byte[] t)
    {
        var p = 0;
        int abstime = 0;
        var running = 0;
        byte At(int i) => i >= 0 && i < t.Length ? t[i] : (byte)0;

        while (p < t.Length)
        {
            unchecked { abstime += (int)ReadVlq(t, ref p); }
            var prefix = $"{track}, {abstime}, ";
            int status;
            if ((At(p) & 0x80) != 0)
            {
                status = At(p++);
                if (status < 0xF0) running = status;
            }
            else status = running;

            var ch = status & 0x0F;
            switch (status & 0xF0)
            {
                case 0x80: w.Line($"{prefix}Note_off_c, {ch}, {At(p)}, {At(p + 1)}"); p += 2; continue;
                case 0x90: w.Line($"{prefix}Note_on_c, {ch}, {At(p)}, {At(p + 1)}"); p += 2; continue;
                case 0xA0: w.Line($"{prefix}Poly_aftertouch_c, {ch}, {At(p)}, {At(p + 1)}"); p += 2; continue;
                case 0xB0: w.Line($"{prefix}Control_c, {ch}, {At(p)}, {At(p + 1)}"); p += 2; continue;
                case 0xC0: w.Line($"{prefix}Program_c, {ch}, {At(p)}"); p += 1; continue;
                case 0xD0: w.Line($"{prefix}Channel_aftertouch_c, {ch}, {At(p)}"); p += 1; continue;
                case 0xE0: w.Line($"{prefix}Pitch_bend_c, {ch}, {(At(p + 1) << 7) | At(p)}"); p += 2; continue;
            }

            if (status is 0xF0 or 0xF7)
            {
                var len = (int)ReadVlq(t, ref p);
                var name = status == 0xF0 ? "System_exclusive" : "System_exclusive_packet";
                w.Line($"{prefix}{name}, {len}{Bytes(t, p, len)}");
                p += len;
                continue;
            }

            if (status != 0xFF)
            {
                w.Line($"{prefix}Unknown_event, {status:X2}x");
                continue;
            }

            var type = At(p++);
            var mlen = (int)ReadVlq(t, ref p);
            switch (type)
            {
                case 0x00: w.Line($"{prefix}Sequence_number, {(At(p) << 8) | At(p + 1)}"); break;
                case >= 0x01 and <= 0x07:
                    w.Raw(Encoding.ASCII.GetBytes(prefix + TextNames[type - 1] + ", "));
                    w.Raw(QuoteText(t, p, mlen));
                    w.Raw(new byte[] { (byte)'\r', (byte)'\n' });
                    break;
                case 0x20: w.Line($"{prefix}Channel_prefix, {At(p)}"); break;
                case 0x21: w.Line($"{prefix}MIDI_port, {At(p)}"); break;
                case 0x2F: w.Line($"{prefix}End_track"); return;
                case 0x51: w.Line($"{prefix}Tempo, {(At(p) << 16) | (At(p + 1) << 8) | At(p + 2)}"); break;
                case 0x54: w.Line($"{prefix}SMPTE_offset, {At(p)}, {At(p + 1)}, {At(p + 2)}, {At(p + 3)}, {At(p + 4)}"); break;
                case 0x58: w.Line($"{prefix}Time_signature, {At(p)}, {At(p + 1)}, {At(p + 2)}, {At(p + 3)}"); break;
                case 0x59: w.Line($"{prefix}Key_signature, {(sbyte)At(p)}, \"{(At(p + 1) == 0 ? "major" : "minor")}\""); break;
                case 0x7F: w.Line($"{prefix}Sequencer_specific, {mlen}{Bytes(t, p, mlen)}"); break;
                default: w.Line($"{prefix}Unknown_meta_event, {type}, {mlen}{Bytes(t, p, mlen)}"); break;
            }
            p += mlen;
        }
    }

    private static readonly string[] TextNames =
        { "Text_t", "Copyright_t", "Title_t", "Instrument_name_t", "Lyric_t", "Marker_t", "Cue_point_t" };

    private static long ReadVlq(byte[] t, ref int p)
    {
        long value = 0;
        while (true)
        {
            var b = p < t.Length ? t[p] : (byte)0;
            p++;
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0 || p > t.Length + 4) return value & 0xFFFFFFFF;
        }
    }

    private static string Bytes(byte[] t, int p, int len)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < len; i++) sb.Append(", ").Append(p + i < t.Length ? t[p + i] : 0);
        return sb.ToString();
    }

    private static byte[] QuoteText(byte[] t, int p, int len)
    {
        var o = new List<byte> { (byte)'"' };
        for (var i = 0; i < len; i++)
        {
            var c = p + i < t.Length ? t[p + i] : (byte)0;
            if (c == '"') { o.Add((byte)'"'); o.Add((byte)'"'); }
            else if (c == '\\') { o.Add((byte)'\\'); o.Add((byte)'\\'); }
            else if (c < 0x20 || (c >= 0x7F && c <= 0xA0)) o.AddRange(Encoding.ASCII.GetBytes("\\" + System.Convert.ToString(c, 8).PadLeft(3, '0')));
            else o.Add(c);
        }
        o.Add((byte)'"');
        return o.ToArray();
    }

    private sealed class Writer
    {
        private readonly MemoryStream _s = new();
        public void Line(string s) { Raw(Encoding.ASCII.GetBytes(s)); Raw(new byte[] { (byte)'\r', (byte)'\n' }); }
        public void Raw(byte[] b) => _s.Write(b, 0, b.Length);
        public byte[] ToArray() => _s.ToArray();
    }
}
