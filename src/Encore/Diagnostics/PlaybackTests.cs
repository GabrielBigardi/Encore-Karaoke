using System.Diagnostics;
using System.IO;
using Encore.Core;
using Encore.Services;
using NAudio.Wave;

namespace Encore.Diagnostics;

internal static class PlaybackTests
{
    internal static async Task<TestReport> Run(AppStorage storage)
    {
        var clock = Stopwatch.StartNew();
        var tests = new List<TestItem>();
        void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        async Task Test(string name, Func<Task> action)
        {
            try { await action(); tests.Add(new(name, true)); }
            catch (Exception ex) { tests.Add(new(name, false, ex.ToString())); }
        }

        var original = SongLibrary.Scan([storage.DemoFolder]).Songs.First();
        foreach (var format in new[] { "wav", "mp3", "ogg" })
        {
            var path = original.AudioPath;
            if (format != "wav")
            {
                path = Path.Combine(storage.DemoFolder, "pause-fixture." + format);
                using var resource = typeof(PlaybackTests).Assembly.GetManifestResourceStream("Encore.Diagnostics.Fixtures.tone." + format)!;
                using var file = File.Create(path);
                resource.CopyTo(file);
            }
            var song = new Song { ChartPath = path + ".txt", AudioPath = path, Title = "Pause stress test",
                Artist = "Encore", Bpm = 120, Notes = [new(0, 500, 69, "Test", NoteKind.Standard, 0)], Lines = [] };
            await Test($"{format.ToUpperInvariant()}: rapid pause/resume preserves playback and the frozen clock", async () =>
            {
                using var audio = new AudioPlayer();
                audio.Load(song); audio.Volume = 0; audio.GuideEnabled = true;
                double previous = 0, activeMs = 0;
                using var cancellation = new CancellationTokenSource();
                var polling = Task.Run(async () =>
                {
                    while (!cancellation.IsCancellationRequested) { _ = audio.PositionMs; await Task.Delay(1); }
                });
                try
                {
                    for (var cycle = 0; cycle < 80; cycle++)
                    {
                        var active = Stopwatch.StartNew();
                        audio.Play();
                        var immediate = audio.PositionMs;
                        Assert(immediate <= previous + active.Elapsed.TotalMilliseconds + 100,
                            $"Cycle {cycle}: startup clock jumped from {previous:0.000} to {immediate:0.000} ms.");
                        var delay = new[] { 0, 1, 3, 7 }[cycle % 4];
                        if (delay > 0) await Task.Delay(delay);
                        audio.Pause(); active.Stop(); activeMs += active.Elapsed.TotalMilliseconds;
                        var paused = audio.PositionMs;
                        Assert(audio.Error is null, $"Cycle {cycle}: {audio.Error}");
                        Assert(!audio.Ended, $"Cycle {cycle}: song completed during pause/resume.");
                        Assert(paused >= previous - 1 && paused <= activeMs + 100,
                            $"Cycle {cycle}: position {paused:0.000} ms; total active time {activeMs:0.000} ms.");
                        if (cycle % 8 == 0) await Task.Delay(20);
                        Assert(Math.Abs(audio.PositionMs - paused) < 1, $"Cycle {cycle}: paused clock advanced.");
                        previous = paused;
                    }
                    audio.Play(); await Task.Delay(120);
                    Assert(audio.Error is null && !audio.Ended && audio.PositionMs > previous + 60,
                        $"Playback failed after the rapid toggles: {audio.Error}; position {audio.PositionMs:0.000} ms.");
                    audio.Pause();
                }
                finally { cancellation.Cancel(); await polling; }
            });
        }
        await Test("Long pauses and immediate resumes exclude paused time from the song clock", async () =>
        {
            using var audio = new AudioPlayer();
            audio.Load(original); audio.Volume = 0; audio.GuideEnabled = false;
            for (var cycle = 0; cycle < 4; cycle++)
            {
                audio.Play(); await Task.Delay(180); audio.Pause();
                var paused = audio.PositionMs; await Task.Delay(650);
                Assert(Math.Abs(audio.PositionMs - paused) < 1, "A long pause advanced the audio clock.");
                var elapsed = Stopwatch.StartNew(); audio.Play();
                Assert(audio.PositionMs <= paused + elapsed.Elapsed.TotalMilliseconds + 40, "Resume included paused time.");
                await Task.Delay(180); audio.Pause();
                var advance = audio.PositionMs - paused;
                Assert(advance > 100 && advance < elapsed.Elapsed.TotalMilliseconds + 40,
                    $"Cycle {cycle}: resumed audio advanced {advance:0.000} ms in {elapsed.Elapsed.TotalMilliseconds:0.000} ms.");
                Assert(audio.Error is null && !audio.Ended, "Resume interrupted or completed the song: " + audio.Error);
            }
        });
        await Test("Rapid native pauses preserve queued frames, resampler state and the final sample", async () =>
        {
            // The final partial buffer and rate conversion must survive every pause.
            var source = new CountedSilence(44100, 2, 44100 * 3 + 113);
            using var session = new WasapiPlaybackSession(source);
            for (var cycle = 0; cycle < 120; cycle++)
            {
                session.Play();
                if (cycle % 3 != 0) await Task.Delay(1);
                session.Pause();
                var frames = source.FramesRead;
                if (cycle % 12 == 0) await Task.Delay(20);
                Assert(source.FramesRead == frames, "The decoder continued reading while paused.");
                Assert(!session.Ended && session.Error is null, "A pause interrupted or completed the stream: " + session.Error);
            }
            session.Play(); var completion = Stopwatch.StartNew();
            while (!session.Ended && session.Error is null && completion.Elapsed.TotalSeconds < 4) await Task.Delay(10);
            Assert(session.Ended && session.Error is null, "The stream did not drain normally: " + session.Error);
            Assert(source.FramesRead == source.TotalFrames, "Some source samples were skipped or duplicated.");
            var expectedMs = source.TotalFrames * 1000.0 / source.WaveFormat.SampleRate;
            Assert(Math.Abs(session.PositionMs - expectedMs) < 75,
                $"The output clock ended at {session.PositionMs:0.000} ms for {expectedMs:0.000} ms of decoded samples.");
            session.Dispose(); var finalFrames = source.FramesRead; await Task.Delay(20);
            Assert(source.FramesRead == finalFrames, "The audio worker read from the decoder after disposal.");
        });
        await Test("A song completes only after its final audio buffer drains", async () =>
        {
            var path = Path.Combine(storage.DemoFolder, "pause-fixture.ogg");
            var song = new Song { ChartPath = path + ".txt", AudioPath = path, Title = "Natural end test",
                Artist = "Encore", Bpm = 120, Notes = [new(0, 500, 69, "Test", NoteKind.Standard, 0)], Lines = [] };
            using var audio = new AudioPlayer();
            audio.Load(song); audio.Volume = 0; audio.GuideEnabled = false;
            audio.Play(); await Task.Delay(400); audio.Pause();
            var paused = audio.PositionMs; await Task.Delay(350);
            Assert(!audio.Ended && Math.Abs(audio.PositionMs - paused) < 1, "Pause was treated as natural completion.");
            audio.Play(); var resumed = Stopwatch.StartNew();
            while (!audio.Ended && audio.Error is null && resumed.Elapsed.TotalSeconds < 4) await Task.Delay(10);
            Assert(audio.Error is null && audio.Ended, "Song did not finish normally: " + audio.Error);
            Assert(resumed.Elapsed.TotalMilliseconds > 1000, "Song finished immediately after resume.");
            Assert(Math.Abs(audio.PositionMs - audio.DurationMs) < 75, "Final audio position is incorrect.");
        });
        return new(tests.Count(t => t.Passed), tests.Count(t => !t.Passed), clock.Elapsed.TotalMilliseconds, tests);
    }

    private sealed class CountedSilence : ISampleProvider
    {
        private int framesRead;
        internal int FramesRead => Volatile.Read(ref framesRead);
        internal int TotalFrames { get; }
        public WaveFormat WaveFormat { get; }
        internal CountedSilence(int rate, int channels, int frames)
        { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels); TotalFrames = frames; }
        public int Read(float[] buffer, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > buffer.Length - count || count % WaveFormat.Channels != 0)
                throw new ArgumentException("The playback buffer request is out of bounds or splits a sample frame.");
            var frames = Math.Min(count / WaveFormat.Channels, TotalFrames - FramesRead);
            Array.Clear(buffer, offset, frames * WaveFormat.Channels);
            Interlocked.Add(ref framesRead, frames);
            return frames * WaveFormat.Channels;
        }
    }
}
