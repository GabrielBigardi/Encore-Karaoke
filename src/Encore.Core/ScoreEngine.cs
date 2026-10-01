namespace Encore.Core;

public sealed record ScoreSnapshot(double Score, double Accuracy, double Progress, int Perfect, int Good, int Miss, int Combo, int BestCombo, string Feedback);

/// <summary>Each note is divided into <=50 ms slices, including short notes and fractional tails.
/// Unvoiced and unobserved slices are always misses. Golden notes receive double weight.</summary>
public sealed class ScoreEngine
{
    private sealed record Slice(double TimeMs, double Pitch, double Weight);
    private readonly List<Slice> slices = [];
    private readonly object sync = new();
    private int cursor, perfect, good, miss, combo, bestCombo;
    private double earned, seenWeight;
    private readonly double totalWeight;
    private string feedback = "Ready when you are";

    public ScoreEngine(Song song, int transpose = 0)
    {
        foreach (var note in song.Notes.Where(n => n.Scored))
            for (var start = note.StartMs; start < note.EndMs - .001; start += 50)
            {
                var duration = Math.Min(50, note.EndMs - start);
                slices.Add(new Slice(start + duration / 2, note.Pitch + transpose, note.Weight * duration / 50));
            }
        slices.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        totalWeight = slices.Sum(t => t.Weight);
    }
    public void Advance(double timeMs, PitchReading reading, double readingTimeMs)
    {
        lock (sync)
        {
            while (cursor < slices.Count && slices[cursor].TimeMs <= timeMs)
            {
                var slice = slices[cursor++];
                var voiced = reading.Voiced && Math.Abs(slice.TimeMs - readingTimeMs) <= 35;
                var delta = voiced ? PitchMath.OctaveDistance(reading.Midi, slice.Pitch) : double.PositiveInfinity;
                var points = delta <= .5 ? 1 : delta <= 1 ? .5 : 0;
                earned += points * slice.Weight;
                seenWeight += slice.Weight;
                if (points == 1) { perfect++; combo++; feedback = "Perfect"; }
                else if (points == .5) { good++; combo++; feedback = "Good"; }
                else { miss++; combo = 0; feedback = voiced ? "Find the note" : "Keep singing"; }
                bestCombo = Math.Max(bestCombo, combo);
            }
        }
    }
    public void Finish() => Advance(double.PositiveInfinity, PitchReading.Silent(), double.NegativeInfinity);
    public ScoreSnapshot Snapshot
    {
        get { lock (sync) return new(Percent(earned, totalWeight), Percent(earned, seenWeight), Percent(seenWeight, totalWeight), perfect, good, miss, combo, bestCombo, feedback); }
    }
    private static double Percent(double numerator, double denominator) => denominator <= 0 ? 0 : Math.Clamp(100 * numerator / denominator, 0, 100);
}
