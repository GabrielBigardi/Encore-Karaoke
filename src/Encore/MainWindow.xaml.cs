using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Encore.Controls;
using Encore.Core;
using Encore.Services;
using Microsoft.Win32;

namespace Encore;

public sealed class SongRow : INotifyPropertyChanged
{
    public required Song Song { get; init; }
    public string Title => Song.Title;
    public string Subtitle => Song.Artist + "  ·  " + Song.Genre;
    public string Duration => Song.DurationLabel;
    public string? CoverPath => Song.CoverPath;
    public double? BestScore { get; set; }
    public string BestLabel => BestScore?.ToString("0", CultureInfo.CurrentCulture) ?? "—";
    private bool favorite;
    public bool Favorite { get => favorite; set { favorite=value; PropertyChanged?.Invoke(this,new(nameof(FavoriteGlyph))); } }
    public string FavoriteGlyph => Favorite?"♥":"♡";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainWindow : Window
{
    private readonly AppStorage storage;
    private List<Song> songs=[];
    private List<string> importErrors=[];
    public ObservableCollection<SongRow> SongRows { get; }=[];
    private readonly AudioPlayer audio=new();
    private MicrophoneService? microphone;
    private ScoreEngine? scoring;
    private Song? activeSong;
    private Song? selectedSong => (SongList.SelectedItem as SongRow)?.Song;
    private bool initialized, favoritesOnly, micTest, fullscreen, scanning, updatingMicrophones;
    // 0 idle, 1 countdown, 2 playing, 3 paused. DSP reads this atomically.
    private int performanceState;
    private readonly Stopwatch countdown=new();
    private readonly Stopwatch micTestClock=new();
    private readonly Stopwatch toastClock=new();
    private TimeSpan lastRender=TimeSpan.Zero;
    private WindowState beforeFullscreen;
    private bool captureUnavailable;
    private string currentView="library";
    private LyricLine? renderedLine;
    private int renderedSyllable=-1;
    private List<MicrophoneDevice> devices=[];
    private readonly DispatcherTimer saveDebounce=new() { Interval=TimeSpan.FromMilliseconds(450) };

    public MainWindow(AppStorage storage)
    {
        this.storage=storage;
        InitializeComponent(); DataContext=this;
        GateSlider.Value=storage.Settings.NoiseGate;
        LatencySlider.Value=storage.Settings.LatencyMs;
        VolumeSlider.Value=storage.Settings.Volume;
        GuideCheck.IsChecked=storage.Settings.GuideMelody;
        TransposeBox.ItemsSource=Enumerable.Range(-12,25).Select(x=>new { Value=x,Label=x==0?"Original key":$"{x:+0;-0} semitones" }).ToList();
        TransposeBox.DisplayMemberPath="Label"; TransposeBox.SelectedValuePath="Value"; TransposeBox.SelectedValue=storage.Settings.Transpose;
        saveDebounce.Tick+=(_,_)=>{saveDebounce.Stop();SaveSettings();};
        initialized=true;RefreshAudioLabels();RefreshMicrophones();ShowView("library");RefreshHistory();
        Loaded+=async (_,_)=>
        {
            await RefreshLibraryAsync();
            if(storage.Warning is not null) ShowToast(storage.Warning);
        };
        SizeChanged+=(_,_)=>
        {
            var compact=ActualHeight<810;
            var small=ActualHeight<660;
            HeroRow.Height=new GridLength(small?0:compact?160:210);
            HeroPanel.Visibility=small?Visibility.Collapsed:Visibility.Visible;
            HeroTitle.FontSize=compact?24:29;
            HeroDescription.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            HeroTitle.LineHeight=compact?27:33;
            SelectedArt.Height=compact?95:136;
            SidebarNote.Visibility=small?Visibility.Collapsed:Visibility.Visible;
            SidebarFooterRow.Height=new GridLength(small?0:178);
            ContentRoot.Margin=new Thickness(ActualWidth<1100?20:30,small?16:24,ActualWidth<1100?20:30,small?16:18);
            StageHeaderRow.Height=new GridLength(compact?72:94);
            StageLyricsRow.Height=new GridLength(compact?104:142);
            StageStatsRow.Height=new GridLength(compact?78:94);
            StageControlsRow.Height=new GridLength(compact?62:72);
            StageTitle.FontSize=compact?24:28;
            CurrentLyrics.FontSize=compact?26:32;
        };
        Width=Math.Min(1360,SystemParameters.WorkArea.Width-24);
        Height=Math.Min(900,SystemParameters.WorkArea.Height-24);
        CompositionTarget.Rendering+=Render;
    }
    private void ShowView(string name)
    {
        currentView=name;
        LibraryView.Visibility=name=="library"?Visibility.Visible:Visibility.Collapsed;
        StageView.Visibility=name=="stage"?Visibility.Visible:Visibility.Collapsed;
        SettingsView.Visibility=name=="settings"?Visibility.Visible:Visibility.Collapsed;
        HistoryView.Visibility=name=="history"?Visibility.Visible:Visibility.Collapsed;
        foreach(var (button,id) in new[]{(LibraryNav,"library"),(StageNav,"stage"),(SettingsNav,"settings"),(HistoryNav,"history")})
        {
            button.Background=id==name?new SolidColorBrush(Color.FromRgb(48,35,43)):Brushes.Transparent;
            button.Foreground=id==name?(Brush)FindResource("Accent"):(Brush)FindResource("Muted");
        }
        if(name!="settings"&&micTest)StopMicTest();
    }
    private async Task RefreshLibraryAsync()
    {
        if(scanning)return;scanning=true;
        try
        {
            var adjacent=Path.Combine(AppContext.BaseDirectory,"songs");
            var roots=storage.Settings.SongFolders.Concat(new[]{storage.DemoFolder}).Concat(Directory.Exists(adjacent)?new[]{adjacent}:[]);
            var scan=await Task.Run(()=>SongLibrary.Scan(roots));
            songs=scan.Songs.ToList();importErrors=scan.Errors.ToList();
            ApplyFilter();FolderList.ItemsSource=storage.Settings.SongFolders.ToList();
            if(importErrors.Count>0)ShowInfo("Some songs need attention",$"{songs.Count} songs are ready to sing. These charts could not be loaded:\n\n"+string.Join("\n\n",importErrors.Take(30)));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) {ShowInfo("Your library is unavailable",e.Message);}
        finally{scanning=false;}
    }
    private void ApplyFilter()
    {
        if(!initialized)return;
        var previous=selectedSong?.Id;
        var query=SearchBox.Text.Trim();
        IEnumerable<Song> matching=songs.Where(s=>query.Length==0||s.Title.Contains(query,StringComparison.CurrentCultureIgnoreCase)||s.Artist.Contains(query,StringComparison.CurrentCultureIgnoreCase));
        if(favoritesOnly)matching=matching.Where(s=>storage.Settings.Favorites.Contains(s.Id));
        double? Best(Song s)=>storage.Settings.History.Where(h=>h.SongId==s.Id).Select(h=>(double?)h.Score).Max();
        matching=SortBox.SelectedIndex switch{1=>matching.OrderBy(s=>s.Artist).ThenBy(s=>s.Title),2=>matching.OrderByDescending(s=>Best(s)??-1).ThenBy(s=>s.Title),_=>matching.OrderBy(s=>s.Title)};
        SongRows.Clear();
        foreach(var song in matching)SongRows.Add(new SongRow{Song=song,Favorite=storage.Settings.Favorites.Contains(song.Id),BestScore=Best(song)});
        SongList.SelectedItem=SongRows.FirstOrDefault(r=>r.Song.Id==previous)??SongRows.FirstOrDefault(r=>r.Title=="Neon Skyline")??SongRows.FirstOrDefault();
        SongCountText.Text=$"YOUR COLLECTION  /  {SongRows.Count} {(SongRows.Count==1?"SONG":"SONGS")}";
        EmptyLibrary.Visibility=SongRows.Count==0?Visibility.Visible:Visibility.Collapsed;
        SearchPlaceholder.Visibility=query.Length==0?Visibility.Visible:Visibility.Collapsed;
        SingButton.IsEnabled=SongRows.Count>0;
    }
    private void Song_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(SelectedTitle is null)return;
        var song=selectedSong;
        SelectedTitle.Text=song?.Title??"Choose your song";
        SelectedArtist.Text=song?.Artist??"Your next moment is waiting.";
        SelectedMeta.Text=song is null?"":$"{song.DurationLabel}  ·  {song.Bpm:0.#} BPM  ·  {song.Difficulty}";
        SelectedGenre.Text=song is null?"":song.Genre+"  /  "+song.Language;
        SelectedArt.Seed=song?.Title??"Encore";SelectedArt.CoverPath=song?.CoverPath;
        SingButton.IsEnabled=song is not null;
    }
    private async void AddFolder_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog{Title="Choose your karaoke song folder",Multiselect=false};
        if(dialog.ShowDialog(this)==true)await AddFolderAsync(dialog.FolderName);
    }
    private async Task AddFolderAsync(string folder)
    {
        if(!storage.Settings.SongFolders.Contains(folder,StringComparer.OrdinalIgnoreCase)){storage.Settings.SongFolders.Add(folder);SaveSettings();}
        await RefreshLibraryAsync();ShowToast($"Song folder added · {Path.GetFileName(folder)}");
    }
    private async void OnDrop(object sender,DragEventArgs e)
    {
        if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;
        if(Volatile.Read(ref performanceState)!=0){ShowToast("Return to the library before adding songs.");return;}
        foreach(var path in (string[])e.Data.GetData(DataFormats.FileDrop))
        {
            var folder=Directory.Exists(path)?path:Path.GetDirectoryName(path);
            if(folder is not null&&Directory.Exists(folder))await AddFolderAsync(folder);
        }
    }
    private async void RemoveFolder_Click(object sender,RoutedEventArgs e)
    {
        if((sender as FrameworkElement)?.DataContext is not string folder)return;
        storage.Settings.SongFolders.Remove(folder);SaveSettings();await RefreshLibraryAsync();
        ShowToast("Folder removed from your library. Your files are still in place.");
    }
    private void FavoriteSong_Click(object sender,RoutedEventArgs e)
    {
        if((sender as FrameworkElement)?.DataContext is not SongRow row)return;
        row.Favorite=!row.Favorite;
        if(row.Favorite)storage.Settings.Favorites.Add(row.Song.Id);else storage.Settings.Favorites.Remove(row.Song.Id);
        SaveSettings();if(favoritesOnly)ApplyFilter();e.Handled=true;
    }
    private void Favorites_Click(object sender,RoutedEventArgs e){favoritesOnly=!favoritesOnly;FavoritesFilter.Foreground=favoritesOnly?(Brush)FindResource("Accent"):(Brush)FindResource("Text");ApplyFilter();}
    private void Search_Changed(object sender,TextChangedEventArgs e)=>ApplyFilter();
    private void Sort_Changed(object sender,SelectionChangedEventArgs e)=>ApplyFilter();
    private async void Refresh_Click(object sender,RoutedEventArgs e)=>await RefreshLibraryAsync();
    private void Song_DoubleClick(object sender,MouseButtonEventArgs e){if(e.OriginalSource is FrameworkElement element&&FindAncestor<Button>(element) is null)StartSong();}
    private static T? FindAncestor<T>(DependencyObject child)where T:DependencyObject{for(DependencyObject? element=child;element is not null;element=VisualTreeHelper.GetParent(element))if(element is T value)return value;return null;}

    private void RefreshMicrophones()
    {
        updatingMicrophones=true;
        try
        {
            devices=MicrophoneService.Devices();
            MicrophoneBox.ItemsSource=new[]{new MicrophoneDevice("","Windows default microphone")}.Concat(devices).ToList();
            if(!string.IsNullOrWhiteSpace(storage.Settings.MicrophoneId)&&!devices.Any(d=>d.Id==storage.Settings.MicrophoneId))storage.Settings.MicrophoneId=null;
            MicrophoneBox.SelectedValue=storage.Settings.MicrophoneId??"";
            captureUnavailable=devices.Count==0;
            MicStatusText.Text=captureUnavailable?"●  Connect a microphone to sing":"●  Microphone available";
            MicStatusText.Foreground=new SolidColorBrush(captureUnavailable?Color.FromRgb(220,197,153):Color.FromRgb(157,212,180));
        }
        catch(Exception e){captureUnavailable=true;MicStatusText.Text="●  Microphone setup needed";MicTestStatus.Text=e.Message;}
        finally{updatingMicrophones=false;}
    }
    private void RefreshMic_Click(object sender,RoutedEventArgs e){StopMicTest();RefreshMicrophones();ShowToast("Microphones refreshed.");}
    private void Microphone_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!initialized||updatingMicrophones)return;StopMicTest();storage.Settings.MicrophoneId=MicrophoneBox.SelectedValue as string;ScheduleSave();
    }
    private void TestMic_Click(object sender,RoutedEventArgs e)
    {
        if(micTest){StopMicTest();return;}
        try
        {
            microphone?.Dispose();microphone=new();micTestClock.Restart();
            microphone.Start(storage.Settings.MicrophoneId,storage.Settings.NoiseGate,storage.Settings.LatencyMs,()=>micTestClock.Elapsed.TotalMilliseconds);
            micTest=true;TestMicButton.Content="Stop microphone test";MicTestStatus.Text="Listening. Hum or sing a comfortable, steady note.";
        }
        catch(Exception ex){StopMicTest();ShowMicrophoneError(ex.Message);}
    }
    private void StopMicTest()
    {
        if(!micTest)return;micTest=false;microphone?.Dispose();microphone=null;
        TestMicButton.Content="Test microphone";TestMicLevel.Value=0;TunerNote.Text="—";TunerHz.Text="Ready to listen";MicTestStatus.Text="Test stopped. Your microphone is no longer active.";
    }
    private void ShowMicrophoneError(string reason)
    {
        ExitFullscreen();ShowView("settings");ShowInfo("Let's get your microphone ready","Connect or select a microphone, then try Test microphone.\n\nIn Windows Settings → Privacy (or Privacy & security) → Microphone, enable microphone access and allow desktop apps.\n\nIf the device is busy, close other audio apps and try again.\n\n"+reason);
    }
    private void AudioSetting_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!initialized)return;
        storage.Settings.NoiseGate=GateSlider.Value;storage.Settings.LatencyMs=LatencySlider.Value;storage.Settings.Volume=VolumeSlider.Value;
        audio.Volume=(float)VolumeSlider.Value;
        if(microphone is not null){microphone.NoiseGate=GateSlider.Value;microphone.LatencyMs=LatencySlider.Value;}
        RefreshAudioLabels();ScheduleSave();
    }
    private void RefreshAudioLabels(){GateValue.Text=$"Threshold {GateSlider.Value:0.000} RMS";LatencyValue.Text=$"{LatencySlider.Value:+0;-0;0} ms";VolumeValue.Text=$"{VolumeSlider.Value:P0}";}
    private void Guide_Changed(object sender,RoutedEventArgs e){if(!initialized)return;storage.Settings.GuideMelody=GuideCheck.IsChecked==true;ScheduleSave();}
    private void Transpose_Changed(object sender,SelectionChangedEventArgs e){if(!initialized)return;storage.Settings.Transpose=TransposeBox.SelectedValue is int value?value:0;ScheduleSave();}
    private void ScheduleSave(){saveDebounce.Stop();saveDebounce.Start();}
    private bool SaveSettings(){try{storage.Save();return true;}catch(Exception e)when(e is IOException or UnauthorizedAccessException){ShowToast("Your settings could not be saved: "+e.Message);return false;}}

    private void Sing_Click(object sender,RoutedEventArgs e)=>StartSong();
    private void StartSong()
    {
        var song=selectedSong??activeSong;if(song is null){ShowToast("Add a song folder to start singing.");return;}
        StopPerformance();StopMicTest();
        try
        {
            activeSong=song;scoring=new ScoreEngine(song,storage.Settings.Transpose);
            audio.Load(song);audio.Volume=(float)storage.Settings.Volume;audio.GuideEnabled=storage.Settings.GuideMelody;audio.Transpose=storage.Settings.Transpose;
            microphone=new MicrophoneService((time,reading,readingTime)=>{if(Volatile.Read(ref performanceState)==2)scoring?.Advance(time,reading,readingTime);});
            try{microphone.Start(storage.Settings.MicrophoneId,storage.Settings.NoiseGate,storage.Settings.LatencyMs,()=>audio.PositionMs);}
            catch(Exception ex){StopPerformance();ShowMicrophoneError(ex.Message);return;}
            StageTitle.Text=song.Title;StageArtist.Text=song.Artist+"  ·  "+song.Genre;
            LiveScore.Text=0.0.ToString("0.0");ComboText.Text="0 ×";AccuracyText.Text="0% accuracy";HitFeedback.Text="Ready";PitchText.Text="—";StageMicLevel.Value=0;SongProgress.Value=0;
            StageTime.Text=$"0:00 / {FormatTime(audio.DurationMs)}";
            CountdownOverlay.Visibility=Visibility.Visible;PauseOverlay.Visibility=Visibility.Collapsed;ResultOverlay.Visibility=Visibility.Collapsed;
            CountdownCaption.Text="YOUR MOMENT STARTS NOW";PauseButton.Content="Ⅱ";StageHint.Text="Match the glowing bars. Golden notes count double. Sing in any octave.";
            CurrentLyrics.Inlines.Clear();CurrentLyrics.Text="Take a breath. Find your voice.";NextLyrics.Text=song.Lines.FirstOrDefault()?.Text??"";
            renderedLine=null;renderedSyllable=-1;ShowView("stage");countdown.Restart();Volatile.Write(ref performanceState,1);
        }
        catch(Exception e){StopPerformance();if(e is System.Runtime.InteropServices.COMException||captureUnavailable)ShowMicrophoneError(e.Message);else ShowInfo("This song couldn't start",e.Message+"\n\nCheck the audio file, microphone and speaker connection, then try again.");}
    }
    private void StopPerformance()
    {
        Volatile.Write(ref performanceState,0);countdown.Stop();
        if(!micTest){microphone?.Dispose();microphone=null;}
        audio.Pause();scoring=null;
        if(CountdownOverlay is not null)CountdownOverlay.Visibility=Visibility.Collapsed;
        if(PauseOverlay is not null)PauseOverlay.Visibility=Visibility.Collapsed;
    }
    private void Pause_Click(object sender,RoutedEventArgs e)=>TogglePause();
    private void TogglePause()
    {
        var state=Volatile.Read(ref performanceState);
        if(state==2)
        {
            Volatile.Write(ref performanceState,3);audio.Pause();PauseOverlay.Visibility=Visibility.Visible;PauseButton.Content="▶";
        }
        else if(state==3)
        {
            try{audio.Play();Volatile.Write(ref performanceState,2);PauseOverlay.Visibility=Visibility.Collapsed;PauseButton.Content="Ⅱ";}
            catch(Exception e){ShowInfo("Playback couldn't resume",e.Message);}
        }
    }
    private void Render(object? sender,EventArgs e)
    {
        if(e is not RenderingEventArgs render||render.RenderingTime-lastRender<TimeSpan.FromMilliseconds(15))return;
        lastRender=render.RenderingTime;
        if(Toast.Visibility==Visibility.Visible&&toastClock.Elapsed.TotalSeconds>5)Toast.Visibility=Visibility.Collapsed;
        if(micTest&&microphone is not null)
        {
            var reading=FreshReading();TunerNote.Text=reading.NoteName;TunerHz.Text=reading.Voiced?$"{reading.Frequency:0.0} Hz":"Hum a steady note";
            TestMicLevel.Value=reading.Rms;TunerQuality.Text=reading.Voiced?$"{reading.Confidence:P0} pitch confidence":reading.Rms<storage.Settings.NoiseGate?"Below your noise filter. Sing a little louder.":"Listening for a clear, sustained pitch.";
            if(microphone.Error is not null){var reason=microphone.Error;StopMicTest();ShowMicrophoneError(reason);}
        }
        var state=Volatile.Read(ref performanceState);
        if(state==0)return;
        if(microphone?.Error is not null)
        {
            var reason=microphone.Error;StopPerformance();ShowMicrophoneError(reason);return;
        }
        if(state==1)
        {
            var remaining=3-countdown.Elapsed.TotalSeconds;
            CountdownText.Text=remaining>0?Math.Ceiling(remaining).ToString("0"):"Go";
            PitchCanvas.Update(activeSong,0,new(0,PitchReading.Silent()),storage.Settings.Transpose);
            if(remaining<=0)
            {
                try{audio.Play();Volatile.Write(ref performanceState,2);CountdownOverlay.Visibility=Visibility.Collapsed;}
                catch(Exception ex){StopPerformance();ShowInfo("Your speakers aren't ready",ex.Message+"\n\nConnect an audio output device and try again.");}
            }
            return;
        }
        if(audio.Error is not null){var reason=audio.Error;StopPerformance();ShowInfo("Playback interrupted",reason);return;}
        var time=audio.PositionMs;
        var snapshot=scoring?.Snapshot;
        var readingNow=state==2?FreshReading():PitchReading.Silent();
        PitchCanvas.Update(activeSong,time,new(microphone?.Latest.TimeMs??time,readingNow),storage.Settings.Transpose);
        if(snapshot is not null)
        {
            LiveScore.Text=snapshot.Score.ToString("0.0");ComboText.Text=$"{snapshot.Combo} ×";AccuracyText.Text=$"{snapshot.Accuracy:0}% accuracy";
            HitFeedback.Text=activeSong?.Notes.Any(n=>n.Scored&&n.StartMs<=time&&n.EndMs>time)==true?snapshot.Feedback:"Instrumental";
        }
        PitchText.Text=readingNow.NoteName;StageMicLevel.Value=readingNow.Rms;
        SongProgress.Value=audio.DurationMs>0?time/audio.DurationMs*100:0;StageTime.Text=$"{FormatTime(time)} / {FormatTime(audio.DurationMs)}";
        UpdateLyrics(time);
        if(state==2&&audio.Ended)FinishPerformance();
    }
    private PitchReading FreshReading()
    {
        var latest=microphone?.Latest;
        if(latest is null||Stopwatch.GetElapsedTime(latest.WallTimestamp).TotalMilliseconds>200)return PitchReading.Silent();
        return latest.Reading;
    }
    private void UpdateLyrics(double time)
    {
        if(activeSong is null)return;
        var line=activeSong.LineAt(time);if(line is null)return;
        var syllable=line.Notes.Count(n=>n.StartMs<=time);
        if(line==renderedLine&&syllable==renderedSyllable)return;
        renderedLine=line;renderedSyllable=syllable;CurrentLyrics.Inlines.Clear();
        foreach(var note in line.Notes)
        {
            var brush=note.StartMs<=time?(Brush)FindResource("Accent"):(Brush)FindResource("Text");
            CurrentLyrics.Inlines.Add(new Run(note.Lyric.Replace("~","")){Foreground=brush});
        }
        var index=activeSong.Lines.ToList().IndexOf(line);NextLyrics.Text=index+1<activeSong.Lines.Count?activeSong.Lines[index+1].Text:"Every note counts. Make this one yours.";
    }
    private void FinishPerformance()
    {
        if(activeSong is null||scoring is null)return;
        var lastPitch=microphone?.Latest;
        if(lastPitch is not null)scoring.Advance(audio.PositionMs,FreshReading(),lastPitch.TimeMs);
        scoring.Finish();var result=scoring.Snapshot;var song=activeSong;
        var previous=storage.Settings.History.Where(h=>h.SongId==song.Id).Select(h=>h.Score).DefaultIfEmpty(-1).Max();
        var duration=audio.DurationMs/1000;StopPerformance();
        var saved=true;
        try{storage.AddResult(new(song.Id,song.Title,song.Artist,DateTimeOffset.Now,result.Score,result.Perfect,result.Good,result.Miss,result.BestCombo,duration));}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){saved=false;ShowToast("The performance couldn't be saved: "+e.Message);}
        ResultHeading.Text=result.Score>=90?"What an encore.":result.Score>=70?"You found your rhythm.":result.Score>=40?"Your voice is coming through.":"Every voice starts somewhere.";
        ResultSong.Text=song.Title+"  ·  "+song.Artist;ResultScore.Text=result.Score.ToString("0.0");
        ResultMessage.Text=result.Score>=90?"Beautifully in tune. The stage is yours.":result.Score>=70?"A strong performance. Keep chasing those perfect notes.":result.Score>=40?"Listen for the melody and let your next note settle into place.":"Try the melody guide and a microphone check. Then make it yours.";
        ResultPerfect.Text=result.Perfect.ToString();ResultGood.Text=result.Good.ToString();ResultMiss.Text=result.Miss.ToString();ResultCombo.Text=$"{result.BestCombo}×";
        ResultBest.Text=!saved?"Score shown here; history couldn't be saved.":result.Score>previous&&previous>=0?"✦  A new personal best. Performance saved.":"Performance saved to your history.";
        ResultOverlay.Visibility=Visibility.Visible;ApplyFilter();RefreshHistory();
    }
    private void RefreshHistory()
    {
        var history=storage.Settings.History;HistoryList.ItemsSource=history.ToList();
        EmptyHistory.Visibility=history.Count==0?Visibility.Visible:Visibility.Collapsed;
        HistoryCount.Text=history.Count.ToString();HistoryBest.Text=history.Count==0?"—":history.Max(h=>h.Score).ToString("0.0");HistoryAverage.Text=history.Count==0?"—":history.Average(h=>h.Score).ToString("0.0");
    }
    private void ExportHistory_Click(object sender,RoutedEventArgs e)
    {
        if(storage.Settings.History.Count==0){ShowToast("Finish your first song to export your results.");return;}
        var dialog=new SaveFileDialog{Title="Export your performances",Filter="CSV file (*.csv)|*.csv",FileName="Encore performances.csv"};
        if(dialog.ShowDialog(this)!=true)return;
        string Csv(string value)=>"\""+(value.Length>0&&"=+-@".Contains(value[0])?"'":"")+value.Replace("\"","\"\"")+"\"";
        var csv=new StringBuilder("Date,Title,Artist,Score,Perfect,Good,Miss,Best combo\r\n");
        foreach(var h in storage.Settings.History)csv.AppendLine(string.Join(',',h.Date.ToString("O"),Csv(h.Title),Csv(h.Artist),h.Score.ToString("0.00",CultureInfo.InvariantCulture),h.Perfect,h.Good,h.Miss,h.BestCombo));
        try{File.WriteAllText(dialog.FileName,csv.ToString(),new UTF8Encoding(true));ShowToast("Your performances have been exported.");}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){ShowInfo("Export couldn't be saved",ex.Message);}
    }

    private void Library_Click(object sender,RoutedEventArgs e){StopPerformance();ResultOverlay.Visibility=Visibility.Collapsed;ExitFullscreen();ShowView("library");}
    private void Stage_Click(object sender,RoutedEventArgs e){if(Volatile.Read(ref performanceState)!=0)ShowView("stage");else StartSong();}
    private void History_Click(object sender,RoutedEventArgs e){StopPerformance();ExitFullscreen();ShowView("history");RefreshHistory();}
    private void Settings_Click(object sender,RoutedEventArgs e){StopPerformance();ExitFullscreen();ShowView("settings");RefreshMicrophones();}
    private void ResultLibrary_Click(object sender,RoutedEventArgs e)=>Library_Click(sender,e);
    private void SingAgain_Click(object sender,RoutedEventArgs e){ResultOverlay.Visibility=Visibility.Collapsed;StartSong();}
    private void FullScreen_Click(object sender,RoutedEventArgs e)=>ToggleFullscreen();
    private void ToggleFullscreen()
    {
        if(fullscreen){ExitFullscreen();return;}
        beforeFullscreen=WindowState;fullscreen=true;Sidebar.Visibility=Visibility.Collapsed;SidebarColumn.Width=new GridLength(0);WindowState=WindowState.Maximized;
    }
    private void ExitFullscreen(){if(!fullscreen)return;fullscreen=false;Sidebar.Visibility=Visibility.Visible;SidebarColumn.Width=new GridLength(208);WindowState=beforeFullscreen;}
    private void Minimize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void Maximize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void OnKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape)
        {
            if(InfoOverlay.Visibility==Visibility.Visible){InfoOverlay.Visibility=Visibility.Collapsed;return;}
            if(ResultOverlay.Visibility==Visibility.Visible){Library_Click(this,new());return;}
            if(fullscreen){ExitFullscreen();return;}
            if(currentView=="stage")Library_Click(this,new());
        }
        if(InfoOverlay.Visibility==Visibility.Visible||ResultOverlay.Visibility==Visibility.Visible)return;
        if(e.Key==Key.F11){ToggleFullscreen();e.Handled=true;}
        else if(e.Key==Key.Space&&currentView=="stage"&&Keyboard.FocusedElement is not TextBox){TogglePause();e.Handled=true;}
        else if(e.Key==Key.Enter&&currentView=="library"&&Keyboard.FocusedElement==SongList){StartSong();e.Handled=true;}
    }
    private void ImportHelp_Click(object sender,RoutedEventArgs e)=>ShowInfo("Bring your music to the stage","Each song needs a local audio file and a matching UltraStar .txt chart in the same folder.\n\nUse Add song folder to select your collection. Subfolders are scanned automatically. You can also drop a folder onto Encore, or create a songs folder next to Encore.exe.\n\nWAV, MP3 and OGG work offline. Additional formats depend on the Windows codecs available. Charts support normal, golden and freestyle notes, quarter-beat timing, and lyric breaks.\n\nGolden notes count double. Freestyle lyrics are shown but never scored. Duet charts use the first singer's part. Legacy relative charts need conversion to absolute timing.\n\nEncore includes three original instrumental practice songs. Enable the melody guide in Studio setup to learn their vocal notes.\n\nA regular audio file alone has no reference melody for scoring; it needs a matching chart.");
    private void ShowInfo(string title,string text){InfoTitle.Text=title;InfoText.Text=text;InfoOverlay.Visibility=Visibility.Visible;}
    private void DismissInfo_Click(object sender,RoutedEventArgs e)=>InfoOverlay.Visibility=Visibility.Collapsed;
    private void ShowToast(string message){ToastText.Text=message;Toast.Visibility=Visibility.Visible;toastClock.Restart();}
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        CompositionTarget.Rendering-=Render;saveDebounce.Stop();Volatile.Write(ref performanceState,0);
        microphone?.Dispose();audio.Dispose();SaveSettings();
    }
    private static string FormatTime(double milliseconds)=>TimeSpan.FromMilliseconds(Math.Max(0,milliseconds)).ToString(@"m\:ss");

    internal void PrepareScreenshot(string view)
    {
        if(view=="stage"&&songs.Count>0)
        {
            activeSong=selectedSong??songs[0];ShowView("stage");StageTitle.Text=activeSong.Title;StageArtist.Text=activeSong.Artist+"  ·  Chart preview";
            CountdownOverlay.Visibility=Visibility.Collapsed;PauseOverlay.Visibility=Visibility.Collapsed;
            PitchCanvas.Update(activeSong,7800,new(7800,PitchReading.Silent()),storage.Settings.Transpose);UpdateLyrics(7800);
            StageTime.Text=$"0:07 / {activeSong.DurationLabel}";SongProgress.Value=7800/activeSong.EndMs*100;
            StageHint.Text="Chart preview · Microphone and playback are inactive.";
        }
        else if(view=="settings")ShowView("settings");else if(view=="history")ShowView("history");
    }
    internal async Task<Diagnostics.TestReport> RunInteractionTests()
    {
        var stopwatch=Stopwatch.StartNew();var tests=new List<Diagnostics.TestItem>();
        void Test(string name,Action action){try{action();tests.Add(new(name,true));}catch(Exception ex){tests.Add(new(name,false,ex.Message));}}
        void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        Test("Embedded songs appear and selection updates detail panel",()=>{Assert(SongRows.Count==3,"Practice songs missing.");SongList.SelectedItem=SongRows.First(r=>r.Title=="Neon Skyline");Assert(SelectedTitle.Text=="Neon Skyline","Selection detail is stale.");});
        Test("Search filters songs and restores the full collection",()=>{SearchBox.Text="golden";Assert(SongRows.Count==1&&SongRows[0].Title=="Golden Hour","Search failed.");SearchBox.Text="";Assert(SongRows.Count==3,"Search reset failed.");});
        Test("Favorites persist and the favorites filter works",()=>
        {
            var row=SongRows.First(r=>r.Title=="Neon Skyline");if(!row.Favorite)FavoriteSong_Click(new Button{DataContext=row},new RoutedEventArgs(Button.ClickEvent));
            Favorites_Click(this,new());Assert(SongRows.Count==1&&SongRows[0].Favorite,"Favorites filter failed.");Favorites_Click(this,new());
            Assert(storage.Settings.Favorites.Contains(row.Song.Id),"Favorite didn't persist.");
        });
        Test("Song sorting changes title and artist order",()=>{SortBox.SelectedIndex=0;Assert(SongRows[0].Title=="Golden Hour","Title ordering failed.");SortBox.SelectedIndex=1;Assert(SongRows.Count==3,"Artist sort dropped songs.");});
        Test("Studio controls update and save the audio settings",()=>{Settings_Click(this,new());GateSlider.Value=.02;LatencySlider.Value=40;VolumeSlider.Value=.5;GuideCheck.IsChecked=false;TransposeBox.SelectedValue=2;SaveSettings();Assert(storage.Settings.NoiseGate==.02&&storage.Settings.LatencyMs==40&&storage.Settings.Volume==.5&&!storage.Settings.GuideMelody&&storage.Settings.Transpose==2,"Audio controls did not update.");});
        Test("Completed session displays and persists its actual zero score",()=>
        {
            SongList.SelectedItem=SongRows.First(r=>r.Title=="Neon Skyline");activeSong=selectedSong;scoring=new ScoreEngine(activeSong!);FinishPerformance();
            Assert(ResultOverlay.Visibility==Visibility.Visible&&ResultScore.Text==0.0.ToString("0.0"),"Results overlay failed.");
            Assert(storage.Settings.History.Count>0&&storage.Settings.History[0].Score==0&&storage.Settings.History[0].Miss>0,"History lost the result.");
            ResultLibrary_Click(this,new());Assert(ResultOverlay.Visibility==Visibility.Collapsed&&LibraryView.Visibility==Visibility.Visible,"Results navigation failed.");
        });
        Test("Performance history renders saved records and summary",()=>{History_Click(this,new());Assert(HistoryList.Items.Count>0&&EmptyHistory.Visibility==Visibility.Collapsed,"History view is empty.");Assert(HistoryBest.Text==0.0.ToString("0.0"),"History summary is wrong.");Library_Click(this,new());});
        Test("Full screen toggles and restores the window layout",()=>{ToggleFullscreen();Assert(SidebarColumn.Width.Value==0&&Sidebar.Visibility==Visibility.Collapsed,"Full screen failed.");ExitFullscreen();Assert(SidebarColumn.Width.Value==208&&Sidebar.Visibility==Visibility.Visible,"Layout was not restored.");});
        Test("Compact window keeps the library and song action usable",()=>{Width=1080;Height=720;UpdateLayout();Assert(HeroRow.Height.Value==160&&SelectedArt.Height==95,"Compact layout not applied.");Assert(SongList.ActualHeight>80,"Library disappeared at the compact window size.");});
        Test("Small laptop window keeps the stage and singing button visible",()=>{Width=900;Height=560;UpdateLayout();Assert(HeroPanel.Visibility==Visibility.Collapsed&&SidebarNote.Visibility==Visibility.Collapsed,"Small layout didn't adapt.");Assert(SongList.ActualHeight>150&&SingButton.ActualHeight>25,"Library or song action disappeared.");PrepareScreenshot("stage");UpdateLayout();Assert(PitchCanvas.ActualHeight>100,"Pitch display disappeared on a small screen.");Library_Click(this,new());});
        Test("Microphone and melody controls display readable labels",()=>{Settings_Click(this,new());MicrophoneBox.ApplyTemplate();TransposeBox.ApplyTemplate();Assert(MicrophoneBox.Text=="Windows default microphone","Microphone label is not readable.");Assert(TransposeBox.Text=="+2 semitones","Melody practice key label is not readable.");Library_Click(this,new());});
        Test("Refreshing microphones preserves a selected connected device",()=>{if(devices.Count==0)return;MicrophoneBox.SelectedValue=devices[0].Id;var chosen=storage.Settings.MicrophoneId;RefreshMicrophones();Assert(MicrophoneBox.SelectedValue as string==chosen&&storage.Settings.MicrophoneId==chosen,"Microphone selection was reset by refresh.");MicrophoneBox.SelectedValue="";});
        await Task.Delay(50);
        return new(tests.Count(t=>t.Passed),tests.Count(t=>!t.Passed),stopwatch.Elapsed.TotalMilliseconds,tests);
    }
}
