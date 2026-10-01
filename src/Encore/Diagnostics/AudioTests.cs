using Encore.Services;

namespace Encore.Diagnostics;

public sealed record AudioTestReport(bool Passed,IReadOnlyList<TestItem> Tests,IReadOnlyList<MicrophoneDevice> Devices);
public static class AudioTests
{
    public static async Task<AudioTestReport> Run(AppStorage storage)
    {
        var results=new List<TestItem>();var devices=new List<MicrophoneDevice>();
        try
        {
            devices=MicrophoneService.Devices();results.Add(new("Enumerate native WASAPI capture devices",true));
            var song=SongLibrary.Scan(new[]{storage.DemoFolder}).Songs.First();
            using var audio=new AudioPlayer();audio.Load(song);audio.Volume=.02f;audio.GuideEnabled=false;audio.Play();
            await Task.Delay(850);var position=audio.PositionMs;
            results.Add(new("Playback clock advances at the output device",position>500&&position<1200,$"Position {position:0} ms"));
            audio.Pause();var paused=audio.PositionMs;await Task.Delay(450);
            results.Add(new("Pause freezes playback position",Math.Abs(audio.PositionMs-paused)<1));
            audio.Play();await Task.Delay(500);
            results.Add(new("Resume continues from the paused audio position",audio.PositionMs>paused+300&&audio.PositionMs<paused+800,$"Position {audio.PositionMs:0} ms; paused at {paused:0} ms"));
            audio.Pause();
            if(devices.Count>0)
            {
                using var mic=new MicrophoneService();var clock=System.Diagnostics.Stopwatch.StartNew();
                mic.Start(null,.015,0,()=>clock.Elapsed.TotalMilliseconds);await Task.Delay(700);
                results.Add(new("WASAPI capture and separate DSP worker produce frames",mic.Latest.WallTimestamp>0&&mic.Error is null,mic.Error));
            }
            else results.Add(new("WASAPI microphone capture",false,"No microphone is connected on the test computer."));
        }
        catch(Exception ex){results.Add(new("Native audio integration",false,ex.ToString()));}
        return new(results.All(r=>r.Passed),results,devices);
    }
}
