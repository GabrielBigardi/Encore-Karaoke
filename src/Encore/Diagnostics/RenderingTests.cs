using System.Diagnostics;
using System.Windows.Media;
using Encore.Controls;

namespace Encore.Diagnostics;

public sealed record RenderingTestReport(bool Passed, double ElapsedMs, int CompositionFrames,
    long NoteUpdates, long DrawnFrames, double CompositionFps, double NoteUpdateFps,
    double DrawnFps, int RenderingTier, IReadOnlyList<TestItem> Tests);

internal static class RenderingTests
{
    internal static async Task<RenderingTestReport> Run(PitchRoll notes)
    {
        // Allow the compositor and the silent WASAPI playback clock to settle.
        await Task.Delay(750);
        var clock = Stopwatch.StartNew();
        var firstUpdate = notes.FrameUpdates;
        var firstDraw = notes.DrawnFrames;
        var frames = 0;
        TimeSpan? lastFrame = null;
        void Observe(object? sender, EventArgs args)
        {
            if (args is not RenderingEventArgs frame || lastFrame == frame.RenderingTime) return;
            lastFrame = frame.RenderingTime;
            frames++;
        }

        CompositionTarget.Rendering += Observe;
        try { await Task.Delay(3000); }
        finally { CompositionTarget.Rendering -= Observe; clock.Stop(); }

        var updates = notes.FrameUpdates - firstUpdate;
        var draws = notes.DrawnFrames - firstDraw;
        var tests = new List<TestItem>
        {
            new("Windows composition produces distinct rendering frames", frames > 0),
            new("The note display accepts every available composition frame", updates >= frames - 1,
                $"{updates} note updates / {frames} composition frames"),
            new("Accepted note updates are drawn without a fixed frame cap", draws >= updates - 2,
                $"{draws} drawn frames / {updates} note updates")
        };
        var seconds = clock.Elapsed.TotalSeconds;
        return new(tests.All(t => t.Passed), clock.Elapsed.TotalMilliseconds, frames, updates, draws,
            frames / seconds, updates / seconds, draws / seconds, RenderCapability.Tier >> 16, tests);
    }
}
