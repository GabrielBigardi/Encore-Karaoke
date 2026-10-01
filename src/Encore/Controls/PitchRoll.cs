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
    private readonly Brush muted=new SolidColorBrush(Color.FromRgb(109,96,128));
    private readonly Brush lavender=new SolidColorBrush(Color.FromRgb(182,163,255));
    private readonly Brush coral=new SolidColorBrush(Color.FromRgb(255,156,135));
    private readonly Brush gold=new SolidColorBrush(Color.FromRgb(244,202,119));
    public void Update(Song? song,double time,TimedReading pitch,int transpose)
    {
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
        var notes=song?.Notes??[];
        var pitches=notes.Where(n=>n.Scored).Select(n=>n.Pitch+transpose).ToArray();
        double min=pitches.Length==0?59:Math.Floor(pitches.Min()-3.0),max=pitches.Length==0?75:Math.Ceiling(pitches.Max()+3.0);
        if(max-min<12){var padding=(12-(max-min))/2;min-=padding;max+=padding;}
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
            var y=Y(pitch);var opacity=pitch%12==0?38:18;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)opacity,177,150,206)),1),new Point(left,y),new Point(right,y));
        }
        var beatMs=60000/(song?.Bpm??120);
        for(var beat=Math.Floor((time-2000)/beatMs);beat*beatMs<time+5600;beat++)
        {
            var x=X(beat*beatMs);
            if(x<left)continue;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)(beat%4==0?25:10),190,161,215)),1),new Point(x,top),new Point(x,bottom));
        }
        dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(15,160,120,185),Colors.Transparent,0),null,new Rect(now,top,right-now,bottom-top));
        foreach(var note in notes)
        {
            if(note.EndMs<time-2300||note.StartMs>time+5800)continue;
            var x=X(note.StartMs);var end=X(note.EndMs);var y=Y(note.Pitch+transpose);
            var active=note.StartMs<=time&&note.EndMs>time;
            var color=note.Kind switch{NoteKind.Golden=>Color.FromRgb(244,202,119),NoteKind.Freestyle=>Color.FromRgb(109,96,137),_=>Color.FromRgb(176,152,231)};
            var rect=new Rect(x,y-Math.Max(4,rowHeight*.27),Math.Max(3,end-x-2),Math.Max(8,rowHeight*.54));
            if(active)
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(20,color.R,color.G,color.B)),null,new Rect(rect.X-5,rect.Y-5,rect.Width+10,rect.Height+10),8,8);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(30,color.R,color.G,color.B)),null,new Rect(rect.X-2,rect.Y-2,rect.Width+4,rect.Height+4),6,6);
            }
            var fill=new LinearGradientBrush(Color.FromArgb((byte)(active?225:note.EndMs<time?55:140),color.R,color.G,color.B),Color.FromArgb((byte)(active?175:90),color.R,color.G,color.B),90);
            dc.DrawRoundedRectangle(fill,new Pen(new SolidColorBrush(Color.FromArgb((byte)(active?230:85),color.R,color.G,color.B)),1),rect,4,4);
            if(rect.Width>28&&note.Lyric.Trim().Length>0)DrawText(dc,note.Lyric.Replace("~","").Trim(),rect.X+5,rect.Y-17,9,new SolidColorBrush(Color.FromArgb(170,color.R,color.G,color.B)),rect.Width-8);
            if(note.Kind==NoteKind.Golden&&rect.Width>18)DrawText(dc,"✦",rect.Right-13,rect.Y-2,9,new SolidColorBrush(Color.FromRgb(69,49,33)));
        }
        for(var i=1;i<trail.Count;i++)
        {
            var a=trail[i-1];var b=trail[i];if(b.Time-a.Time>100)continue;
            var start=new Point(X(a.Time),Y(a.Pitch));var end=new Point(X(b.Time),Y(b.Pitch));
            var color=b.Hit?Color.FromRgb(255,176,139):Color.FromRgb(243,120,156);
            var opacity=(byte)Math.Clamp(230-(time-b.Time)/15,50,230);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(30,color.R,color.G,color.B)),8),start,end);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(opacity,color.R,color.G,color.B)),2.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},start,end);
        }
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(140,255,172,142)),1.5),new Point(now,top-7),new Point(now,bottom+5));
        if(reading.Voiced)
        {
            var target=TargetAt(time);var y=Y(PitchMath.NearTarget(reading.Midi,target));
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(90,255,171,144),Colors.Transparent),null,new Point(now,y),18,18);
            dc.DrawEllipse(coral,new Pen(Brushes.White,1.2),new Point(now,y),5,5);
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
        var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if(maxWidth is >0){text.MaxTextWidth=maxWidth.Value;text.MaxLineCount=1;text.Trimming=TextTrimming.CharacterEllipsis;}
        dc.DrawText(text,new Point(x,y));
    }
}
public sealed record TimedReading(double Time,PitchReading Reading);
