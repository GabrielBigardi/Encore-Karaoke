using System.Diagnostics;
using System.IO;
using Encore.Core;
using Encore.Services;

namespace Encore.Diagnostics;

public sealed record TestItem(string Name,bool Passed,string? Error=null);
public sealed record TestReport(int Passed,int Failed,double ElapsedMs,IReadOnlyList<TestItem> Tests);
public static class SelfTests
{
    public static TestReport Run()
    {
        var clock=Stopwatch.StartNew();var results=new List<TestItem>();
        void Test(string name,Action action){try{action();results.Add(new(name,true));}catch(Exception e){results.Add(new(name,false,e.Message));}}
        void Equal(double actual,double expected,double tolerance=.001){if(!double.IsFinite(actual)||Math.Abs(actual-expected)>tolerance)throw new Exception($"Expected {expected}, got {actual}.");}
        void Assert(bool condition,string error){if(!condition)throw new Exception(error);}
        var chartPath=Path.Combine(AppContext.BaseDirectory,"fixture","song.txt");
        string Chart(string rows,string extra="",string bpm="120")=>$"#TITLE:Test\n#ARTIST:Encore\n#MP3:song.wav\n#BPM:{bpm}\n#GAP:1000\n{extra}\n{rows}\nE";
        Song Parse(string rows,string extra="")=>ChartParser.Parse(Chart(rows,extra),chartPath);
        PitchReading Voice(double midi)=>new(PitchMath.MidiToHertz(midi),midi,.99,.1);

        Test("Quarter-beat timing and GAP",()=>{var song=Parse(": 4 8 64 Hello ");Equal(song.Notes[0].StartMs,1500);Equal(song.Notes[0].EndMs,2500);});
        Test("Decimal-comma BPM and negative beats",()=>{var song=ChartParser.Parse(Chart(": -4 8 64 Hello ",bpm:"120,0"),chartPath);Equal(song.Notes[0].StartMs,500);});
        Test("UTF-8 BOM, golden, freestyle and line breaks",()=>{var song=ChartParser.Parse("\uFEFF"+Chart(": 0 4 64 Hel\n* 4 4 67 lo \n- 9\nF 10 4 65 world "),chartPath);Assert(song.Lines.Count==2,"Line breaks lost.");Assert(song.Notes[1].Kind==NoteKind.Golden,"Golden note lost.");Assert(!song.Notes[2].Scored,"Freestyle was scored.");Assert(song.Lines[0].Text=="Hello ","Syllable spaces changed.");});
        Test("Relative C4 and MIDI charts have identical pitch classes",()=>{var a=Parse(": 0 4 4 Test");var b=Parse(": 0 4 64 Test");Equal(PitchMath.OctaveDistance(a.Notes[0].Pitch,b.Notes[0].Pitch),0);});
        Test("Duet chart selects first singer",()=>{var song=Parse("P1\n: 0 4 64 One \nP2\n: 0 4 69 Two ");Assert(song.Notes.Count==1&&song.Notes[0].Lyric=="One ","Wrong duet part.");});
        Test("Malformed charts reject invalid tempo and overlapping notes",()=>
        {
            foreach(var text in new[]{Chart(": 0 4 64 Test",bpm:"NaN"),Chart(": 0 8 64 One\n: 4 4 65 Two"),Chart("nonsense"),Chart(": 0 4 64 Test","#RELATIVE:YES")})
            {var rejected=false;try{ChartParser.Parse(text,chartPath);}catch(FormatException){rejected=true;}Assert(rejected,"Invalid chart was accepted.");}
        });
        Test("Audio path cannot escape song folder",()=>{var rejected=false;try{ChartParser.Parse(Chart(": 0 4 64 Test").Replace("song.wav","../escape.wav"),chartPath);}catch(FormatException){rejected=true;}Assert(rejected,"Traversal accepted.");});
        Test("Hz, MIDI and multiple octave equivalence",()=>{Equal(PitchMath.HertzToMidi(440),69);Equal(PitchMath.MidiToHertz(69),440);Equal(PitchMath.OctaveDistance(40,76),0);Equal(PitchMath.OctaveDistance(59.8,60),.2);});
        Test("YIN vocal range at 48 kHz",()=>
        {
            var detector=new YinDetector();
            foreach(var frequency in new[]{82.4069,110,164.8138,220,440,880})
            {
                var samples=Enumerable.Range(0,2048).Select(i=>(float)(.18*Math.Sin(2*Math.PI*frequency*i/48000))).ToArray();
                var pitch=detector.Detect(samples);Assert(pitch.Voiced,$"No voiced pitch at {frequency} Hz.");Equal(pitch.Midi,PitchMath.HertzToMidi(frequency),.05);
            }
        });
        Test("YIN 44.1 kHz and strong upper harmonics",()=>
        {
            foreach(var rate in new[]{44100,48000})
            {
                var samples=Enumerable.Range(0,2048).Select(i=>(float)(.10*Math.Sin(2*Math.PI*220*i/rate)+.19*Math.Sin(2*Math.PI*440*i/rate)+.08*Math.Sin(2*Math.PI*660*i/rate))).ToArray();
                var pitch=new YinDetector(rate).Detect(samples);Assert(pitch.Voiced,"No harmonic pitch.");Equal(pitch.Midi,57,.08);
            }
        });
        Test("Silence, low amplitude, DC and noise are unvoiced",()=>
        {
            var detector=new YinDetector();var random=new Random(82);
            foreach(var samples in new[]{new float[2048],Enumerable.Repeat(.2f,2048).ToArray(),Enumerable.Range(0,2048).Select(i=>(float)(.005*Math.Sin(2*Math.PI*440*i/48000))).ToArray(),Enumerable.Range(0,2048).Select(_=>(float)(random.NextDouble()*.3-.15)).ToArray()})Assert(!detector.Detect(samples).Voiced,"Unvoiced input identified as pitch.");
        });
        Test("Perfect, good and miss tolerance boundaries",()=>
        {
            foreach(var (delta,expected) in new[]{(0.0,100.0),(.5,100.0),(.5001,50.0),(1.0,50.0),(1.0001,0.0)})
            {
                var song=Parse(": 0 4 64 Test");var engine=new ScoreEngine(song);var target=song.Notes[0].Pitch;
                for(double time=1025;time<1500;time+=50)engine.Advance(time,Voice(target+delta),time);
                engine.Finish();Equal(engine.Snapshot.Score,expected);
            }
        });
        Test("Full-song denominator and complete silence is zero",()=>
        {
            var song=Parse(": 0 8 64 Test");var engine=new ScoreEngine(song);
            for(double time=1025;time<1500;time+=50)engine.Advance(time,Voice(song.Notes[0].Pitch),time);
            Equal(engine.Snapshot.Score,50);Equal(engine.Snapshot.Accuracy,100);engine.Finish();Equal(engine.Snapshot.Score,50);
            var silent=new ScoreEngine(song);silent.Finish();Equal(silent.Snapshot.Score,0);Assert(silent.Snapshot.Miss==20,"Silence tick count wrong.");
        });
        Test("Stale microphone frames cannot earn points",()=>{var song=Parse(": 0 4 64 Test");var engine=new ScoreEngine(song);engine.Advance(1500,Voice(song.Notes[0].Pitch),0);Equal(engine.Snapshot.Score,0);});
        Test("Golden notes weighted twice; freestyle excluded",()=>
        {
            var song=Parse(": 0 4 64 One \n* 4 4 64 Two \nF 8 4 64 Free");var engine=new ScoreEngine(song);
            for(double time=1525;time<2000;time+=50)engine.Advance(time,Voice(song.Notes[1].Pitch),time);
            engine.Finish();Equal(engine.Snapshot.Score,100.0*2/3);Assert(engine.Snapshot.Perfect==10&&engine.Snapshot.Miss==10,"Freestyle changed tick counts.");
        });
        Test("Short notes and partial slices remain scored",()=>
        {
            var song=ChartParser.Parse(Chart(": 0 1 64 Short",bpm:"400"),chartPath);var engine=new ScoreEngine(song);
            engine.Advance(song.Notes[0].EndMs,Voice(song.Notes[0].Pitch),song.Notes[0].StartMs+18.75);engine.Finish();Equal(engine.Snapshot.Score,100);
        });
        Test("Repeated clock and pause intervals cannot double count",()=>
        {
            var song=Parse(": 0 4 64 Test");var engine=new ScoreEngine(song);var reading=Voice(song.Notes[0].Pitch);
            engine.Advance(1025,reading,1025);for(var i=0;i<100;i++)engine.Advance(1025,reading,1025);
            Assert(engine.Snapshot.Perfect==1,"Paused clock added ticks.");engine.Finish();Assert(engine.Snapshot.Perfect==1,"Finish altered hits.");
        });
        Test("Unavailable and future audio timestamps cannot jump the playback clock",()=>
        {
            Equal(WasapiPlaybackSession.CorrelatePosition(0,0,120000,120000),0);
            Equal(WasapiPlaybackSession.CorrelatePosition(0,120005,120010,120000),0);
            Equal(WasapiPlaybackSession.CorrelatePosition(1000,0,120010,120000),1000);
            Equal(WasapiPlaybackSession.CorrelatePosition(1000,120020,120010,120000),1000);
        });
        Test("Audio timestamps from before resume cannot include paused time",()=>
        {
            Equal(WasapiPlaybackSession.CorrelatePosition(1000,119000,120010,120000),1000);
            Equal(WasapiPlaybackSession.CorrelatePosition(1000,119995,120010,120000),1000);
            Equal(WasapiPlaybackSession.CorrelatePosition(1000,120005,120010,120000),1005);
        });
        Test("Combo breaks on a miss and retains personal maximum",()=>
        {
            var song=Parse(": 0 4 64 Test");var engine=new ScoreEngine(song);var voice=Voice(song.Notes[0].Pitch);
            engine.Advance(1025,voice,1025);engine.Advance(1075,voice,1075);engine.Advance(1125,PitchReading.Silent(),1125);
            Assert(engine.Snapshot.Combo==0&&engine.Snapshot.BestCombo==2,"Combo incorrect.");
        });
        Test("Settings and results round-trip to local JSON",()=>
        {
            var folder=Path.Combine(Path.GetTempPath(),"encore-test-"+Guid.NewGuid().ToString("N"));
            try
            {
                var storage=new AppStorage(folder);storage.Settings.Volume=.45;storage.Settings.Favorites.Add("test");
                storage.AddResult(new("test","Title","Artist",DateTimeOffset.UtcNow,87.5,2,1,0,3,30));
                var reloaded=new AppStorage(folder);Equal(reloaded.Settings.Volume,.45);Assert(reloaded.Settings.History.Count==1&&reloaded.Settings.Favorites.Contains("test"),"Persistence lost data.");
            }
            finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
        });
        Test("Packaged original songs parse and find their audio",()=>
        {
            var folder=Path.Combine(Path.GetTempPath(),"encore-demos-"+Guid.NewGuid().ToString("N"));
            try
            {
                var storage=new AppStorage(folder);storage.InstallDemos();var scan=SongLibrary.Scan(new[]{storage.DemoFolder});
                Assert(scan.Songs.Count==3,$"Expected 3 embedded songs, got {scan.Songs.Count}; {string.Join("; ",scan.Errors)}. Resources: {string.Join(", ",typeof(SelfTests).Assembly.GetManifestResourceNames())}");
                Assert(scan.Errors.Count==0,"Practice charts failed.");
                foreach(var song in scan.Songs){using var player=new AudioPlayer();player.Load(song);Assert(player.DurationMs>=song.EndMs,"Audio too short.");}
            }
            finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
        });
        foreach(var extension in new[]{"mp3","ogg"})Test($"Bundled {extension.ToUpperInvariant()} decoder reads and seeks without system codecs",()=>
        {
            var fixture=Path.Combine(Path.GetTempPath(),"encore-codec-"+Guid.NewGuid().ToString("N")+"."+extension);
            try
            {
                using(var resource=typeof(SelfTests).Assembly.GetManifestResourceStream("Encore.Diagnostics.Fixtures.tone."+extension)!)
                using(var file=File.Create(fixture))resource.CopyTo(file);
                using var reader=AudioPlayer.OpenReader(fixture);
                Assert(reader.TotalTime.TotalSeconds>1.9&&reader.TotalTime.TotalSeconds<2.2,"Decoded duration is wrong.");
                var buffer=new byte[reader.WaveFormat.AverageBytesPerSecond/5];
                Assert(reader.Read(buffer,0,buffer.Length)>0&&buffer.Any(b=>b!=0),"No decoded audio.");
                reader.CurrentTime=TimeSpan.FromSeconds(1);
                Equal(reader.CurrentTime.TotalSeconds,1,.001);
                Assert(reader.Read(buffer,0,buffer.Length)>0,"Seeking failed.");
                Assert(reader.CurrentTime.TotalSeconds>=1,"Seek moved backwards.");
            }
            finally{if(File.Exists(fixture))File.Delete(fixture);}
        });
        return new(results.Count(r=>r.Passed),results.Count(r=>!r.Passed),clock.Elapsed.TotalMilliseconds,results);
    }
}
