namespace Encore.Core;

public enum NoteKind { Standard, Golden, Freestyle }
public sealed record ChartNote(double StartMs, double EndMs, int Pitch, string Lyric, NoteKind Kind, int Line)
{
    public bool Scored => Kind != NoteKind.Freestyle;
    public double Weight => Kind == NoteKind.Golden ? 2 : 1;
}

public sealed record LyricLine(int Index, double StartMs, double EndMs, IReadOnlyList<ChartNote> Notes)
{
    public string Text => string.Concat(Notes.Select(n => n.Lyric)).Replace("~", "");
}

public sealed class Song
{
    public required string ChartPath { get; init; }
    public required string AudioPath { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string Genre { get; init; } = "Karaoke";
    public string Language { get; init; } = "";
    public string? CoverPath { get; init; }
    public double Bpm { get; init; }
    public double GapMs { get; init; }
    public required IReadOnlyList<ChartNote> Notes { get; init; }
    public required IReadOnlyList<LyricLine> Lines { get; init; }
    public double EndMs => Notes.Count == 0 ? 0 : Notes.Max(n => n.EndMs);
    public string DurationLabel => TimeSpan.FromMilliseconds(EndMs).ToString(@"m\:ss");
    public string Id => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ChartPath.ToUpperInvariant())))[..20];
    public string Difficulty => Notes.Count(n => n.Scored) < 60 ? "Easy" : Notes.Count(n => n.Scored) < 180 ? "Medium" : "Advanced";
    public LyricLine? LineAt(double timeMs) => Lines.FirstOrDefault(l => l.EndMs > timeMs) ?? Lines.LastOrDefault();
}

public sealed record PitchReading(double Frequency, double Midi, double Confidence, double Rms)
{
    public bool Voiced => Frequency > 0 && Confidence >= .85;
    public static PitchReading Silent(double rms = 0) => new(0, 0, 0, rms);
    public string NoteName => Voiced ? PitchMath.NoteName(Midi) : "—";
}

public static class PitchMath
{
    private static readonly string[] Names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];
    public static double HertzToMidi(double hz) => 69 + 12 * Math.Log2(hz / 440);
    public static double MidiToHertz(double midi) => 440 * Math.Pow(2, (midi - 69) / 12);
    public static double OctaveDistance(double actual, double target)
    {
        var delta = ((actual - target) % 12 + 12) % 12;
        return Math.Min(delta, 12 - delta);
    }
    public static double NearTarget(double actual, double target) => actual + Math.Round((target - actual) / 12) * 12;
    public static string NoteName(double midi)
    {
        var note = (int)Math.Round(midi);
        return Names[(note % 12 + 12) % 12] + (note / 12 - 1);
    }
}

public sealed record PerformanceResult(string SongId, string Title, string Artist, DateTimeOffset Date, double Score, int Perfect, int Good, int Miss, int BestCombo, double DurationSeconds);
public sealed class UserSettings
{
    public List<string> SongFolders { get; set; } = [];
    public HashSet<string> Favorites { get; set; } = [];
    public List<PerformanceResult> History { get; set; } = [];
    public string? MicrophoneId { get; set; }
    public double NoiseGate { get; set; } = .015;
    public double LatencyMs { get; set; } = 0;
    public double Volume { get; set; } = .7;
    public bool GuideMelody { get; set; } = true;
    public int Transpose { get; set; } = 0;
}
