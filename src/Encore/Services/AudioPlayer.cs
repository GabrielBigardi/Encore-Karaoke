using System.IO;
using Encore.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Encore.Services;

/// <summary>Playback position comes from the WASAPI output clock, never the decoder's prefetch position.
/// Pausing recreates the stream at the frozen position so silence cannot advance the audio clock.</summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly object sync = new();
    private WaveStream? reader;
    private WasapiOut? output;
    private MMDevice? device;
    private GuideProvider? provider;
    private Song? song;
    private double baseMs, frozenMs, lastMs;
    private bool playing, ended;
    private int generation;
    private float volume = .7f;
    public bool GuideEnabled { get; set; } = true;
    public int Transpose { get; set; }
    public double DurationMs { get; private set; }
    public bool Ended { get { lock (sync) return ended; } }
    public string? Error { get; private set; }
    public float Volume { get => volume; set { volume = Math.Clamp(value, 0, 1); if (provider is not null) provider.Volume = volume; } }
    public double PositionMs
    {
        get
        {
            lock (sync)
            {
                if (!playing || output is null) return frozenMs;
                try
                {
                    if (output.PlaybackState == PlaybackState.Playing)
                        lastMs = Math.Max(lastMs, Math.Min(DurationMs, baseMs + output.GetPosition() * 1000.0 / output.OutputWaveFormat.AverageBytesPerSecond));
                }
                catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ObjectDisposedException) { Error = e.Message; }
                return lastMs;
            }
        }
    }
    public void Load(Song value)
    {
        lock (sync)
        {
            Release();
            song = value;
            reader = OpenReader(value.AudioPath);
            DurationMs = reader.TotalTime.TotalMilliseconds;
            if (DurationMs + 75 < song.EndMs)
            {
                Release();
                throw new InvalidDataException("The audio ends before the chart's final note. Check #BPM, #GAP, and the selected audio file.");
            }
            baseMs = frozenMs = lastMs = 0;
            ended = false; Error = null;
        }
    }
    internal static WaveStream OpenReader(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ogg" => new VorbisWaveStream(path),
        ".mp3" => new MpegWaveStream(path),
        ".wav" => new WaveFileReader(path),
        _ => new AudioFileReader(path)
    };
    public void Play()
    {
        lock (sync)
        {
            if (reader is null || song is null || playing) return;
            reader.CurrentTime = TimeSpan.FromMilliseconds(frozenMs);
            baseMs = lastMs = frozenMs;
            var captureGeneration = ++generation;
            using var enumerator = new MMDeviceEnumerator();
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            output = new WasapiOut(device, AudioClientShareMode.Shared, true, 40);
            provider = new GuideProvider(reader.ToSampleProvider(), song, baseMs, GuideEnabled, Transpose) { Volume = volume };
            output.Init(provider.ToWaveProvider());
            output.PlaybackStopped += (_, args) =>
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    lock (sync)
                    {
                        if (captureGeneration != generation) return;
                        Error = args.Exception?.Message;
                        frozenMs = args.Exception is null ? DurationMs : lastMs;
                        playing = false;
                        ended = args.Exception is null;
                    }
                });
            };
            playing = true;
            output.Play();
        }
    }
    public void Pause()
    {
        lock (sync)
        {
            frozenMs = PositionMs;
            playing = false;
            ++generation;
            output?.Stop(); output?.Dispose(); output = null;
            device?.Dispose(); device = null;
        }
    }
    private void Release()
    {
        ++generation; playing = false;
        output?.Stop(); output?.Dispose(); output = null;
        device?.Dispose(); device = null;
        reader?.Dispose(); reader = null;
    }
    public void Dispose() { lock (sync) Release(); }

    private sealed class GuideProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly Song song;
        private readonly bool guide;
        private readonly int transpose;
        private long frame;
        private readonly double offsetMs;
        private int noteIndex;
        private double phase;
        public float Volume { get; set; }
        public WaveFormat WaveFormat => source.WaveFormat;
        public GuideProvider(ISampleProvider source, Song song, double offsetMs, bool guide, int transpose)
        { this.source = source; this.song = song; this.offsetMs = offsetMs; this.guide = guide; this.transpose = transpose; }
        public int Read(float[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            var channels = WaveFormat.Channels;
            for (var i = 0; i < read; i += channels, frame++)
            {
                var time = offsetMs + frame * 1000.0 / WaveFormat.SampleRate;
                while (noteIndex < song.Notes.Count && song.Notes[noteIndex].EndMs <= time) noteIndex++;
                double tone = 0;
                if (guide && noteIndex < song.Notes.Count)
                {
                    var note = song.Notes[noteIndex];
                    if (note.Scored && time >= note.StartMs && time < note.EndMs)
                    {
                        var envelope = Math.Min(1, (time - note.StartMs) / 18) * Math.Min(1, (note.EndMs - time) / 35);
                        phase += 2 * Math.PI * PitchMath.MidiToHertz(note.Pitch + transpose) / WaveFormat.SampleRate;
                        if (phase > 2*Math.PI) phase -= 2*Math.PI;
                        tone = .10 * (Math.Sin(phase) + .15 * Math.Sin(2*phase)) * envelope;
                    }
                }
                for (var c = 0; c < channels && i+c < read; c++) buffer[offset+i+c] = (float)Math.Clamp((buffer[offset+i+c] + tone) * Volume, -1, 1);
            }
            return read;
        }
    }

    private sealed class VorbisWaveStream : WaveStream
    {
        private readonly NVorbis.VorbisReader vorbis;
        private float[] samples = [];
        public override WaveFormat WaveFormat { get; }
        public override long Length => vorbis.TotalSamples * WaveFormat.BlockAlign;
        public override long Position { get => vorbis.SamplePosition * WaveFormat.BlockAlign; set => vorbis.SamplePosition = Math.Clamp(value / WaveFormat.BlockAlign, 0, vorbis.TotalSamples); }
        public VorbisWaveStream(string path) { vorbis = new NVorbis.VorbisReader(path); WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(vorbis.SampleRate, vorbis.Channels); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var length = count/4;
            if (samples.Length < length) samples = new float[length];
            var read = vorbis.ReadSamples(samples, 0, length);
            Buffer.BlockCopy(samples, 0, buffer, offset, read*4);
            return read*4;
        }
        protected override void Dispose(bool disposing) { if (disposing) vorbis.Dispose(); base.Dispose(disposing); }
    }

    private sealed class MpegWaveStream : WaveStream
    {
        private readonly NLayer.MpegFile mpeg;
        public override WaveFormat WaveFormat { get; }
        public override long Length => mpeg.Length;
        public override long Position
        {
            get => mpeg.Position;
            set
            {
                var target=Math.Clamp(value,0,Length)/WaveFormat.BlockAlign*WaveFormat.BlockAlign;
                // MPEG decoders seek to frame boundaries. Decode the small remainder so
                // the actual samples resume at the exact device-clock position.
                mpeg.Position=Math.Max(0,target-1152L*WaveFormat.BlockAlign);
                var discard=new byte[1152*WaveFormat.BlockAlign];
                while(mpeg.Position<target)
                {
                    var missing=(int)Math.Min(discard.Length,target-mpeg.Position);
                    if(mpeg.ReadSamples(discard,0,missing)==0)break;
                }
            }
        }
        public MpegWaveStream(string path) { mpeg=new NLayer.MpegFile(path);WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(mpeg.SampleRate,mpeg.Channels); }
        public override int Read(byte[] buffer,int offset,int count) => mpeg.ReadSamples(buffer,offset,count);
        protected override void Dispose(bool disposing) { if(disposing)mpeg.Dispose();base.Dispose(disposing); }
    }
}
