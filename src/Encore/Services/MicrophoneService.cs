using System.Buffers;
using System.Threading.Channels;
using Encore.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Encore.Services;

public sealed record MicrophoneDevice(string Id, string Name);
public sealed record TimedPitch(double TimeMs, PitchReading Reading, long WallTimestamp);

/// <summary>Capture only copies PCM into a bounded FIFO. A dedicated analysis thread converts
/// samples, performs YIN, and advances scoring, independently of WPF's rendering thread.</summary>
public sealed class MicrophoneService : IDisposable
{
    private sealed record Packet(byte[] Data, int Length, double EndMs, long Timestamp);
    private readonly Channel<Packet> queue = Channel.CreateBounded<Packet>(new BoundedChannelOptions(12) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource cancel = new();
    private WasapiCapture? capture;
    private MMDevice? device;
    private Thread? worker;
    private Func<double> clock = () => 0;
    private readonly Action<double, PitchReading, double>? score;
    private volatile TimedPitch latest = new(0, PitchReading.Silent(), 0);
    private volatile string? error;
    private double gate, latency;
    private bool stopping;
    public TimedPitch Latest => latest;
    public string? Error => error;
    public bool Running { get; private set; }
    public double NoiseGate { get => Volatile.Read(ref gate); set => Volatile.Write(ref gate, value); }
    public double LatencyMs { get => Volatile.Read(ref latency); set => Volatile.Write(ref latency, value); }
    public MicrophoneService(Action<double, PitchReading, double>? score = null) { this.score = score; }
    public static List<MicrophoneDevice> Devices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<MicrophoneDevice>();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            using (endpoint) result.Add(new(endpoint.ID, endpoint.FriendlyName));
        return result;
    }
    public void Start(string? id, double noiseGate, double latencyMs, Func<double> timestamp)
    {
        NoiseGate = noiseGate; LatencyMs = latencyMs; clock = timestamp;
        using var enumerator = new MMDeviceEnumerator();
        device = string.IsNullOrWhiteSpace(id) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console) : enumerator.GetDevice(id);
        capture = new WasapiCapture(device, true, 20) { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1) };
        capture.DataAvailable += OnData;
        capture.RecordingStopped += (_, args) => { if (!stopping) { error = args.Exception?.Message ?? "The microphone stopped. Reconnect it and try again."; Running = false; } };
        worker = new Thread(Analyze) { IsBackground = true, Name = "Encore pitch analysis", Priority = ThreadPriority.AboveNormal };
        worker.Start();
        try { capture.StartRecording(); Running = true; }
        catch { Dispose(); throw; }
    }
    private void OnData(object? sender, WaveInEventArgs args)
    {
        if (stopping || args.BytesRecorded <= 0) return;
        var data = ArrayPool<byte>.Shared.Rent(args.BytesRecorded);
        Buffer.BlockCopy(args.Buffer, 0, data, 0, args.BytesRecorded);
        var packet = new Packet(data, args.BytesRecorded, clock(), System.Diagnostics.Stopwatch.GetTimestamp());
        if (!queue.Writer.TryWrite(packet)) ArrayPool<byte>.Shared.Return(data);
    }
    private void Analyze()
    {
        var window = new float[2048];
        var ring = new float[2048];
        var writeIndex = 0;
        var filled = 0;
        var samplesSinceAnalysis = 0;
        var detector = new YinDetector();
        var lastSampleMs = double.NegativeInfinity;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                var packet = queue.Reader.ReadAsync(cancel.Token).AsTask().GetAwaiter().GetResult();
                try
                {
                    var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(packet.Data.AsSpan(0, packet.Length));
                    var startMs = packet.EndMs - samples.Length*1000.0/48000;
                    // Flush old windows after a pause or a capture discontinuity.
                    if (startMs < lastSampleMs - 10 || startMs - lastSampleMs > 100) { filled = 0; samplesSinceAnalysis = 0; }
                    for (var i = 0; i < samples.Length; i++)
                    {
                        ring[writeIndex] = samples[i];
                        writeIndex = (writeIndex + 1) % ring.Length;
                        filled = Math.Min(filled + 1, window.Length);
                        if (++samplesSinceAnalysis < 960 || filled < window.Length) continue;
                        samplesSinceAnalysis = 0;
                        ring.AsSpan(writeIndex).CopyTo(window);
                        ring.AsSpan(0, writeIndex).CopyTo(window.AsSpan(ring.Length-writeIndex));
                        var reading = detector.Detect(window, NoiseGate);
                        var time = startMs + (i+1)*1000.0/48000 - window.Length*500.0/48000 - LatencyMs;
                        latest = new TimedPitch(time, reading, packet.Timestamp);
                        score?.Invoke(time, reading, time);
                    }
                    lastSampleMs = packet.EndMs;
                }
                finally { ArrayPool<byte>.Shared.Return(packet.Data); }
            }
        }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException) { }
        catch (Exception ex) { error = ex.Message; Running = false; }
    }
    public void Dispose()
    {
        stopping = true; Running = false;
        if (capture is not null) { capture.DataAvailable -= OnData; capture.StopRecording(); capture.Dispose(); capture = null; }
        cancel.Cancel(); queue.Writer.TryComplete();
        worker?.Join(1500);
        while (queue.Reader.TryRead(out var packet)) ArrayPool<byte>.Shared.Return(packet.Data);
        device?.Dispose(); device = null;
    }
}
