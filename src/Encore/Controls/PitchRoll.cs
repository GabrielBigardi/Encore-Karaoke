using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Encore.Core;

namespace Encore.Controls;

public sealed class PitchRoll : FrameworkElement
{
    private Song? song;
    private double time, lastTrailTime=double.NegativeInfinity;
    private PitchReading reading=PitchReading.Silent();
    private readonly List<(double Time,double Pitch,bool Hit)> trail=[];
    private int transpose;
    internal long FrameUpdates { get; private set; }
    internal long DrawnFrames { get; private set; }
    private double minPitch=59,maxPitch=75,cachedWidth,cachedHeight,cachedDpi;
    private readonly Dictionary<TextKey,FormattedText> textCache=[];
    private readonly Dictionary<(bool Hit,byte Opacity),Pen> trailPens=[];
    private static readonly Typeface typeface=new("Segoe UI");
    private static readonly Brush muted=Freeze(new SolidColorBrush(Color.FromRgb(109,96,128)));
    private static readonly Brush lavender=Freeze(new SolidColorBrush(Color.FromRgb(182,163,255)));
    private static readonly Brush coral=Freeze(new SolidColorBrush(Color.FromRgb(255,156,135)));
    private static readonly Brush gold=Freeze(new SolidColorBrush(Color.FromRgb(244,202,119)));
    private static readonly Brush goldenStar=Freeze(new SolidColorBrush(Color.FromRgb(69,49,33)));
    private static readonly Brush aheadShade=Freeze(new LinearGradientBrush(Color.FromArgb(15,160,120,185),Colors.Transparent,0));
    private static readonly Brush voiceGlow=Freeze(new RadialGradientBrush(Color.FromArgb(90,255,171,144),Colors.Transparent));
    private static readonly Pen rowPen=Freeze(new Pen(new SolidColorBrush(Color.FromArgb(18,177,150,206)),1));
    private static readonly Pen octavePen=Freeze(new Pen(new SolidColorBrush(Color.FromArgb(38,177,150,206)),1));
    private static readonly Pen beatPen=Freeze(new Pen(new SolidColorBrush(Color.FromArgb(10,190,161,215)),1));
    private static readonly Pen measurePen=Freeze(new Pen(new SolidColorBrush(Color.FromArgb(25,190,161,215)),1));
    private static readonly Pen cursorPen=Freeze(new Pen(new SolidColorBrush(Color.FromArgb(140,255,172,142)),1.5));
    private static readonly Pen voicePen=Freeze(new Pen(Brushes.White,1.2));
    private static readonly Pen hitGlow=Freeze(new Pen(new SolidColorBrush(Color.FromArgb(30,255,176,139)),8));
    private static readonly Pen missGlow=Freeze(new Pen(new SolidColorBrush(Color.FromArgb(30,243,120,156)),8));
    private static readonly NoteStyle[] noteStyles=
    [
        CreateNoteStyle(Color.FromRgb(176,152,231)),
        CreateNoteStyle(Color.FromRgb(244,202,119)),
        CreateNoteStyle(Color.FromRgb(109,96,137))
    ];
    public void Update(Song? song,double time,TimedReading pitch,int transpose)
    {
        FrameUpdates++;
        if(this.song!=song||this.transpose!=transpose)
        {
            var pitches=(song?.Notes??[]).Where(n=>n.Scored).Select(n=>n.Pitch+transpose).ToArray();
            minPitch=pitches.Length==0?59:Math.Floor(pitches.Min()-3.0);
            maxPitch=pitches.Length==0?75:Math.Ceiling(pitches.Max()+3.0);
            if(maxPitch-minPitch<12){var padding=(12-(maxPitch-minPitch))/2;minPitch-=padding;maxPitch+=padding;}
            textCache.Clear();
        }
        if(this.song!=song||time<this.time-50) {trail.Clear();lastTrailTime=double.NegativeInfinity;}
        this.song=song;this.time=time;reading=pitch.Reading;this.transpose=transpose;
        var target=TargetAt(pitch.Time);
        if(reading.Voiced&&pitch.Time>lastTrailTime+.1)
        {
            lastTrailTime=pitch.Time;
            trail.Add((pitch.Time,PitchMath.NearTarget(reading.Midi,target),PitchMath.OctaveDistance(reading.Midi,target)<=.5));
        }
        trail.RemoveAll(p=>p.Time<time-4200);
        InvalidateVisual();
    }
    private double TargetAt(double timeMs) => (song?.Notes.FirstOrDefault(n=>n.StartMs<=timeMs&&n.EndMs>timeMs)?.Pitch ?? song?.Notes.FirstOrDefault(n=>n.EndMs>timeMs)?.Pitch ?? 65)+transpose;
    protected override void OnRender(DrawingContext dc)
    {
        var w=ActualWidth;var h=ActualHeight;if(w<100||h<100)return;
        DrawnFrames++;
        var notes=song?.Notes??[];
        var pixelsPerDip=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if(cachedWidth!=w||cachedHeight!=h||cachedDpi!=pixelsPerDip)
        {
            textCache.Clear();cachedWidth=w;cachedHeight=h;cachedDpi=pixelsPerDip;
        }
        var min=minPitch;var max=maxPitch;
        var top=53.0;var bottom=h-40;var left=55.0;var right=w-24;var now=left+(right-left)*.27;
        var scale=(right-now)/5600;
        double X(double ms)=>now+(ms-time)*scale;
        double Y(double pitch)=>bottom-(pitch-min)/(max-min)*(bottom-top);
        var rowHeight=(bottom-top)/(max-min);
        DrawText(dc,"P I T C H  S T A G E",16,15,9,muted);
        dc.DrawRoundedRectangle(lavender,null,new Rect(w-243,18,16,4),2,2);DrawText(dc,"Target",w-219,11,10,muted);
        dc.DrawEllipse(coral,null,new Point(w-140,20),3,3);DrawText(dc,"Your voice",w-128,11,10,muted);
        dc.DrawRoundedRectangle(gold,null,new Rect(w-55,18,12,4),2,2);DrawText(dc,"★",w-37,10,11,gold);
        dc.PushClip(new RectangleGeometry(new Rect(left,top-10,right-left,bottom-top+22)));
        for(var pitch=(int)Math.Ceiling(min);pitch<=max;pitch++)
        {
            var y=Y(pitch);
            dc.DrawLine(pitch%12==0?octavePen:rowPen,new Point(left,y),new Point(right,y));
        }
        var beatMs=60000/(song?.Bpm??120);
        for(var beat=Math.Floor((time-2000)/beatMs);beat*beatMs<time+5600;beat++)
        {
            var x=X(beat*beatMs);
            if(x<left)continue;
            dc.DrawLine(beat%4==0?measurePen:beatPen,new Point(x,top),new Point(x,bottom));
        }
        dc.DrawRectangle(aheadShade,null,new Rect(now,top,right-now,bottom-top));
        foreach(var note in notes)
        {
            if(note.EndMs<time-2300||note.StartMs>time+5800)continue;
            var x=X(note.StartMs);var y=Y(note.Pitch+transpose);
            var active=note.StartMs<=time&&note.EndMs>time;
            var style=noteStyles[(int)note.Kind];
            var rect=new Rect(x,y-Math.Max(4,rowHeight*.27),Math.Max(3,(note.EndMs-note.StartMs)*scale-2),Math.Max(8,rowHeight*.54));
            if(active)
            {
                dc.DrawRoundedRectangle(style.FaintGlow,null,new Rect(rect.X-5,rect.Y-5,rect.Width+10,rect.Height+10),8,8);
                dc.DrawRoundedRectangle(style.StrongGlow,null,new Rect(rect.X-2,rect.Y-2,rect.Width+4,rect.Height+4),6,6);
            }
            var fill=active?style.Active:note.EndMs<time?style.Past:style.Future;
            dc.DrawRoundedRectangle(fill,active?style.ActiveBorder:style.Border,rect,4,4);
            if(rect.Width>28&&note.Lyric.Trim().Length>0)DrawText(dc,note.Lyric.Replace("~","").Trim(),rect.X+5,rect.Y-17,9,style.Lyric,rect.Width-8);
            if(note.Kind==NoteKind.Golden&&rect.Width>18)DrawText(dc,"✦",rect.Right-13,rect.Y-2,9,goldenStar);
        }
        for(var i=1;i<trail.Count;i++)
        {
            var a=trail[i-1];var b=trail[i];if(b.Time-a.Time>100)continue;
            var start=new Point(X(a.Time),Y(a.Pitch));var end=new Point(X(b.Time),Y(b.Pitch));
            var opacity=(byte)Math.Clamp(230-(time-b.Time)/15,50,230);
            if(!trailPens.TryGetValue((b.Hit,opacity),out var pen))
            {
                var color=b.Hit?Color.FromRgb(255,176,139):Color.FromRgb(243,120,156);
                pen=Freeze(new Pen(new SolidColorBrush(Color.FromArgb(opacity,color.R,color.G,color.B)),2.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round});
                trailPens.Add((b.Hit,opacity),pen);
            }
            dc.DrawLine(b.Hit?hitGlow:missGlow,start,end);
            dc.DrawLine(pen,start,end);
        }
        dc.DrawLine(cursorPen,new Point(now,top-7),new Point(now,bottom+5));
        if(reading.Voiced)
        {
            var target=TargetAt(time);var y=Y(PitchMath.NearTarget(reading.Midi,target));
            dc.DrawEllipse(voiceGlow,null,new Point(now,y),18,18);
            dc.DrawEllipse(coral,voicePen,new Point(now,y),5,5);
        }
        dc.Pop();
        for(var pitch=(int)Math.Ceiling(min);pitch<=max;pitch++)
            if(pitch%2==0)DrawText(dc,PitchMath.NoteName(pitch),17,Y(pitch)-7,9,muted);
        DrawText(dc,"NOW",now-12,bottom+18,8,coral);
        DrawText(dc,"SING IN YOUR OWN OCTAVE",left,bottom+18,8,muted);
        DrawText(dc,"+5s",right-20,bottom+18,8,muted);
    }
    private void DrawText(DrawingContext dc,string value,double x,double y,double size,Brush brush,double? maxWidth=null)
    {
        var key=new TextKey(value,size,((SolidColorBrush)brush).Color,maxWidth);
        if(!textCache.TryGetValue(key,out var text))
        {
            if(textCache.Count>=1024)textCache.Clear();
            text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,typeface,size,brush,cachedDpi);
            if(maxWidth is >0){text.MaxTextWidth=maxWidth.Value;text.MaxLineCount=1;text.Trimming=TextTrimming.CharacterEllipsis;}
            textCache.Add(key,text);
        }
        dc.DrawText(text,new Point(x,y));
    }
    private readonly record struct TextKey(string Value,double Size,Color Color,double? MaxWidth);
    private sealed record NoteStyle(Brush Future,Brush Active,Brush Past,Pen Border,Pen ActiveBorder,Brush FaintGlow,Brush StrongGlow,Brush Lyric);
    private static NoteStyle CreateNoteStyle(Color color)
    {
        Brush Fill(byte start,byte end)=>Freeze(new LinearGradientBrush(Color.FromArgb(start,color.R,color.G,color.B),Color.FromArgb(end,color.R,color.G,color.B),90));
        Brush Solid(byte opacity)=>Freeze(new SolidColorBrush(Color.FromArgb(opacity,color.R,color.G,color.B)));
        return new(Fill(140,90),Fill(225,175),Fill(55,90),Freeze(new Pen(Solid(85),1)),Freeze(new Pen(Solid(230),1)),Solid(20),Solid(30),Solid(170));
    }
    private static T Freeze<T>(T resource)where T:Freezable{resource.Freeze();return resource;}
}
public sealed record TimedReading(double Time,PitchReading Reading);
