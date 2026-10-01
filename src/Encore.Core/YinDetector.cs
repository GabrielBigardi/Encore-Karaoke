namespace Encore.Core;

/// <summary>YIN cumulative mean normalized difference; no FFT peak or harmonic shortcut.</summary>
public sealed class YinDetector
{
    private readonly double[] difference;
    private readonly int sampleRate;
    public int WindowSize { get; }
    public YinDetector(int sampleRate = 48000, int windowSize = 2048)
    {
        this.sampleRate = sampleRate;
        WindowSize = windowSize;
        difference = new double[windowSize / 2];
    }
    public PitchReading Detect(ReadOnlySpan<float> samples, double noiseGate = .015)
    {
        if (samples.Length < WindowSize) return PitchReading.Silent();
        double energy = 0, mean = 0;
        for (var i = 0; i < WindowSize; i++) mean += samples[i];
        mean /= WindowSize;
        for (var i = 0; i < WindowSize; i++) energy += (samples[i] - mean) * (samples[i] - mean);
        var rms = Math.Sqrt(energy / WindowSize);
        if (rms < noiseGate) return PitchReading.Silent(rms);
        var maxTau = Math.Min(difference.Length - 1, sampleRate / 65);
        var minTau = Math.Max(2, sampleRate / 1100);
        var comparisonLength = WindowSize - maxTau;
        difference[0] = 1;
        double cumulative = 0;
        for (var tau = 1; tau <= maxTau; tau++)
        {
            double sum = 0;
            for (var i = 0; i < comparisonLength; i++)
            {
                var delta = samples[i] - samples[i + tau];
                sum += delta * delta;
            }
            cumulative += sum;
            difference[tau] = cumulative <= 1e-20 ? 1 : sum * tau / cumulative;
        }
        int selected = -1;
        for (var tau = minTau; tau < maxTau; tau++)
        {
            if (difference[tau] < .15)
            {
                while (tau + 1 <= maxTau && difference[tau + 1] < difference[tau]) tau++;
                selected = tau;
                break;
            }
        }
        if (selected < 0) return PitchReading.Silent(rms);
        var confidence = 1 - difference[selected];
        if (confidence < .85) return PitchReading.Silent(rms);
        var refined = (double)selected;
        if (selected > 0 && selected < maxTau)
        {
            var left = difference[selected - 1];
            var center = difference[selected];
            var right = difference[selected + 1];
            var denominator = 2 * (2 * center - right - left);
            if (Math.Abs(denominator) > 1e-12) refined += (right - left) / denominator;
        }
        var frequency = sampleRate / refined;
        return new PitchReading(frequency, PitchMath.HertzToMidi(frequency), confidence, rms);
    }
}
