using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Encore.Core;

/// <summary>UltraStar quarter-beat timing: GAP + beat * 60,000 / (BPM * 4).</summary>
public static partial class ChartParser
{
    [GeneratedRegex(@"^([:*FRG])\s+(-?\d+)\s+(\d+)\s+(-?\d+)(?:[ \t](.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex NotePattern();

    public static Song ParseFile(string path)
    {
        // Strict UTF-8 first; legacy charts are commonly Windows-1252.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(1252).GetString(bytes); }
        if (bytes.Length > 2 && ((bytes[0] == 0xff && bytes[1] == 0xfe) || (bytes[0] == 0xfe && bytes[1] == 0xff)))
            text = (bytes[0] == 0xff ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes[2..]);
        return Parse(text, Path.GetFullPath(path));
    }

    public static Song Parse(string text, string path)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<(string Text, int Number)>();
        var number = 0;
        foreach (var raw in text.TrimStart('\uFEFF').Replace("\r", "").Split('\n'))
        {
            number++;
            var line = raw.TrimStart();
            if (line.StartsWith('#'))
            {
                var separator = line.IndexOf(':');
                if (separator > 1) headers[line[1..separator].Trim()] = line[(separator + 1)..].Trim();
            }
            else if (!string.IsNullOrWhiteSpace(line)) rows.Add((line, number));
        }
        string Header(string key, string fallback = "") => headers.GetValueOrDefault(key, fallback);
        double Numeric(string key, double fallback)
        {
            var value = Header(key);
            if (value.Length == 0) return fallback;
            if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !double.IsFinite(result))
                throw new FormatException($"Invalid #{key} value in {Path.GetFileName(path)}.");
            return result;
        }
        var bpm = Numeric("BPM", 0);
        if (bpm <= 0 || bpm > 2000) throw new FormatException("The chart needs a valid #BPM between 0 and 2000.");
        var gap = Numeric("GAP", 0);
        var tickMs = 60000 / (bpm * 4);
        var relative = Header("RELATIVE").Equals("YES", StringComparison.OrdinalIgnoreCase);
        if (relative) throw new FormatException("Legacy #RELATIVE:YES charts need conversion to absolute timing before importing.");
        var notes = new List<ChartNote>();
        int lyricLine = 0, player = 1;
        foreach (var (line, lineNumber) in rows)
        {
            if (line.Trim() == "E") break;
            if (line.StartsWith('P'))
            {
                if (!int.TryParse(line[1..].Trim(), out player)) throw new FormatException($"Invalid player marker on line {lineNumber}.");
                continue;
            }
            if (player == 2) continue;
            if (line.StartsWith('-')) { lyricLine++; continue; }
            var match = NotePattern().Match(line);
            if (!match.Success) throw new FormatException($"Invalid note on line {lineNumber} of {Path.GetFileName(path)}.");
            if (!int.TryParse(match.Groups[2].Value, out var beat) || !int.TryParse(match.Groups[3].Value, out var length) || !int.TryParse(match.Groups[4].Value, out var pitch))
                throw new FormatException($"Note values are too large on line {lineNumber}.");
            if (length == 0) continue;
            var start = gap + beat * tickMs;
            var end = start + length * tickMs;
            if (end <= 0) continue;
            if (end > 7_200_000 || start < -7_200_000 || pitch < -120 || pitch > 180 || notes.Count >= 100000)
                throw new FormatException($"Note is outside the supported range on line {lineNumber}.");
            var kind = match.Groups[1].Value switch { "*" => NoteKind.Golden, "F" or "R" or "G" => NoteKind.Freestyle, _ => NoteKind.Standard };
            notes.Add(new ChartNote(Math.Max(0, start), end, pitch, match.Groups[5].Value, kind, lyricLine));
        }
        if (notes.Count == 0 || !notes.Any(n => n.Scored)) throw new FormatException("The chart contains no scored notes.");
        notes.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        // Standard charts use C4-relative pitches. The supplied specification uses MIDI.
        // Both have the same pitch classes; normalize low chart pitches to a useful register.
        var median = notes.Select(n => n.Pitch).Order().ElementAt(notes.Count / 2);
        var octaveOffset = 12 * (int)Math.Round((65 - median) / 12.0);
        notes = notes.Select(n => n with { Pitch = n.Pitch + octaveOffset }).ToList();
        ChartNote? previous = null;
        foreach (var note in notes.Where(n => n.Scored))
        {
            if (previous is not null && note.StartMs < previous.EndMs - .001)
                throw new FormatException("Scored notes overlap. Import a single singer's chart.");
            previous = note;
        }
        var audioName = Header("AUDIO", Header("MP3"));
        if (audioName.Length == 0) throw new FormatException("The chart needs an #MP3 or #AUDIO filename.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string Resolve(string name)
        {
            var resolved = Path.GetFullPath(Path.Combine(directory, name));
            if (!resolved.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Song assets must be inside the chart's folder.");
            return resolved;
        }
        return new Song
        {
            ChartPath = path, AudioPath = Resolve(audioName), CoverPath = Header("COVER").Length > 0 ? Resolve(Header("COVER")) : null,
            Title = Header("TITLE", Path.GetFileNameWithoutExtension(path)), Artist = Header("ARTIST", "Unknown artist"),
            Genre = Header("GENRE", "Karaoke"), Language = Header("LANGUAGE"), Bpm = bpm, GapMs = gap, Notes = notes,
            Lines = notes.GroupBy(n => n.Line).Select(g => new LyricLine(g.Key, g.Min(n => n.StartMs), g.Max(n => n.EndMs), g.ToList())).OrderBy(l => l.StartMs).ToList()
        };
    }
}
