using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Encore.Services;

/// <summary>One native stream for an entire performance. Stop/Start freezes and
/// resumes the device clock without flushing queued samples or seeking a decoder.</summary>
internal sealed class WasapiPlaybackSession : IDisposable
{
    private readonly object sync = new();
    private readonly AutoResetEvent ready = new(false);
    private readonly MMDevice device;
    private readonly AudioClient client;
    private readonly AudioRenderClient render;
    private readonly AudioClockClient clock;
    private readonly WaveFormat format;
    private readonly ISampleProvider source;
    private readonly float[] samples;
    private readonly byte[] pcm;
    private readonly Thread worker;
    private readonly int capacity;
    private readonly ulong frequency;
    private readonly bool floatingPoint;
    private bool playing, primed, sourceEnded, ended, disposed;
    private double positionMs;
    private long startedAt;
    private double positionAtStart;
    private Exception? failure;

    internal WasapiPlaybackSession(ISampleProvider input)
    {
        MMDevice? openedDevice = null;
        AudioClient? openedClient = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            device = openedDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            client = openedClient = device.AudioClient;
            format = client.MixFormat;
            floatingPoint = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                format is WaveFormatExtensible extended && extended.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
            if (floatingPoint && format.BitsPerSample != 32 || !floatingPoint && format.BitsPerSample is not (8 or 16 or 24 or 32))
                throw new NotSupportedException("The audio output uses an unsupported sample format.");
            source = input.WaveFormat.SampleRate == format.SampleRate ? input : new WdlResamplingSampleProvider(input, format.SampleRate);
            if (source.WaveFormat.Channels != format.Channels) source = new ChannelProvider(source, format.Channels);
            client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.NoPersist,
                40 * TimeSpan.TicksPerMillisecond, 0, format, Guid.Empty);
            client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
            capacity = client.BufferSize;
            samples = new float[capacity * format.Channels];
            pcm = floatingPoint ? [] : new byte[capacity * format.BlockAlign];
            render = client.AudioRenderClient;
            clock = client.AudioClockClient;
            frequency = clock.Frequency;
            if (frequency == 0) throw new InvalidOperationException("The audio output has no usable playback clock.");
            worker = new Thread(RenderAudio) { IsBackground = true, Name = "Encore audio output", Priority = ThreadPriority.AboveNormal };
            worker.Start();
        }
        catch { openedClient?.Dispose(); openedDevice?.Dispose(); ready.Dispose(); throw; }
    }

    internal bool Ended { get { lock (sync) return ended; } }
    internal Exception? Error { get { lock (sync) return failure; } }
    internal double PositionMs
    {
        get
        {
            lock (sync)
            {
                if (!playing || disposed) return positionMs;
                try { return ReadPosition(true); }
                catch (Exception ex) { Fail(ex); return positionMs; }
            }
        }
    }

    internal void Play()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failure is not null) throw new IOException("Playback cannot continue: " + failure.Message, failure);
            if (playing || ended) return;
            if (!primed)
            {
                FillBuffer(capacity); primed = true;
                if (sourceEnded && client.CurrentPadding == 0) { ended = true; return; }
            }
            // This call starts the native clock synchronously. Resumes retain the
            // same audio client, resampler, decoder and queued endpoint samples.
            positionAtStart = positionMs; startedAt = Stopwatch.GetTimestamp();
            client.Start(); playing = true; ready.Set();
        }
    }

    internal void Pause()
    {
        lock (sync)
        {
            if (disposed || !playing) return;
            try
            {
                // Read after Stop so the frozen position includes all samples
                // actually played before the native stream stopped.
                client.Stop(); playing = false; ReadPosition(false);
            }
            catch (Exception ex) { Fail(ex); }
        }
    }

    private double ReadPosition(bool interpolate)
    {
        clock.GetPosition(out var nativePosition, out var qpc);
        var milliseconds = nativePosition * 1000.0 / frequency;
        if (interpolate && nativePosition > 0 && qpc > 0)
        {
            var now = Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency);
            milliseconds = CorrelatePosition(milliseconds, qpc / (double)TimeSpan.TicksPerMillisecond,
                now, startedAt * (1000.0 / Stopwatch.Frequency));
        }
        if (!interpolate || milliseconds <= positionAtStart + Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds + 50)
            positionMs = Math.Max(positionMs, milliseconds);
        return positionMs;
    }

    internal static double CorrelatePosition(double deviceMs, double timestampMs, double nowMs, double startedMs)
    {
        // Some drivers briefly report an unavailable, stale or future timestamp.
        // Only interpolate a recent clock reading from the current playing interval.
        var elapsed = nowMs - timestampMs;
        return deviceMs > 0 && timestampMs > 0 && timestampMs >= startedMs && elapsed >= 0 && elapsed <= 50
            ? deviceMs + elapsed : deviceMs;
    }

    private void RenderAudio()
    {
        while (true)
        {
            ready.WaitOne(50);
            lock (sync)
            {
                if (disposed) return;
                if (!playing) continue;
                try
                {
                    var padding = client.CurrentPadding;
                    if (sourceEnded)
                    {
                        if (padding == 0)
                        {
                            client.Stop(); playing = false; ReadPosition(false); ended = true;
                        }
                    }
                    else if (capacity > padding) FillBuffer(capacity - padding);
                }
                catch (Exception ex) { Fail(ex); }
            }
        }
    }

    private void FillBuffer(int frames)
    {
        var count = frames * format.Channels;
        var read = source.Read(samples, 0, count);
        if (read < 0 || read > count || read % format.Channels != 0)
            throw new InvalidDataException("The audio decoder returned an invalid sample frame.");
        if (read == 0) { sourceEnded = true; return; }
        var writtenFrames = read / format.Channels;
        var buffer = render.GetBuffer(writtenFrames);
        try
        {
            if (floatingPoint) Marshal.Copy(samples, 0, buffer, read);
            else
            {
                var bytes = format.BitsPerSample / 8;
                for (var i = 0; i < read; i++)
                {
                    var sample = Math.Clamp(samples[i], -1, 1);
                    if (bytes == 1) { pcm[i] = (byte)Math.Clamp((int)Math.Round((sample + 1) * 127.5), 0, 255); continue; }
                    var scale = bytes switch { 2 => 32767.0, 3 => 8388607.0, _ => 2147483647.0 };
                    var value = (int)Math.Round(sample * scale);
                    for (var part = 0; part < bytes; part++) pcm[i * bytes + part] = (byte)(value >> (part * 8));
                }
                Marshal.Copy(pcm, 0, buffer, writtenFrames * format.BlockAlign);
            }
        }
        finally { render.ReleaseBuffer(writtenFrames, AudioClientBufferFlags.None); }
    }

    private void Fail(Exception ex)
    {
        failure ??= ex;
        try { if (playing) client.Stop(); } catch (COMException) { }
        playing = false;
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            try { if (playing) client.Stop(); } catch (COMException) { }
            playing = false; disposed = true; ready.Set();
        }
        // The decoder belongs to AudioPlayer. Stop all output reads before the
        // caller disposes it; the worker never takes AudioPlayer's lock.
        worker.Join();
        client.Dispose(); device.Dispose(); ready.Dispose();
    }

    private sealed class ChannelProvider : ISampleProvider
    {
        private readonly ISampleProvider input;
        private float[] buffer = [];
        public WaveFormat WaveFormat { get; }
        internal ChannelProvider(ISampleProvider input, int channels)
        { this.input = input; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(input.WaveFormat.SampleRate, channels); }
        public int Read(float[] output, int offset, int count)
        {
            var inputChannels = input.WaveFormat.Channels; var outputChannels = WaveFormat.Channels;
            var frames = count / outputChannels; var required = frames * inputChannels;
            if (buffer.Length < required) buffer = new float[required];
            var read = input.Read(buffer, 0, required);
            if (read < 0 || read > required || read % inputChannels != 0)
                throw new InvalidDataException("The audio decoder returned an incomplete sample frame.");
            frames = read / inputChannels;
            for (var frame = 0; frame < frames; frame++)
                for (var channel = 0; channel < outputChannels; channel++)
                {
                    float value = 0;
                    if (inputChannels == 1) value = outputChannels <= 2 || channel < 2 ? buffer[frame] : 0;
                    else if (outputChannels >= inputChannels) value = channel < inputChannels ? buffer[frame * inputChannels + channel] : 0;
                    else
                    {
                        var contributors = 0;
                        for (var inputChannel = channel; inputChannel < inputChannels; inputChannel += outputChannels)
                        { value += buffer[frame * inputChannels + inputChannel]; contributors++; }
                        value /= contributors;
                    }
                    output[offset + frame * outputChannels + channel] = value;
                }
            return frames * outputChannels;
        }
    }
}
