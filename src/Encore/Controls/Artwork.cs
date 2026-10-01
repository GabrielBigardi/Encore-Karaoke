using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Encore.Controls;

public sealed class LogoMark : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var gradient = new LinearGradientBrush(Color.FromRgb(250,113,153), Color.FromRgb(255,165,117), 45);
        var heights = new[] { .35, .65, 1, .65, .35 };
        for (int i = 0; i < 5; i++) dc.DrawRoundedRectangle(gradient, null, new Rect(i*ActualWidth/5, (ActualHeight-heights[i]*ActualHeight)/2, ActualWidth/8, heights[i]*ActualHeight), ActualWidth/12, ActualWidth/12);
    }
}

public sealed class HeroArt : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var center = new Point(ActualWidth*.69, ActualHeight*.60);
        dc.PushClip(new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight)));
        dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(50,235,134,156), Color.FromArgb(0,235,134,156)), null, center, 184, 184);
        for (var i = 0; i < 5; i++) dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(34-i*4),226,157,185)), 1), center, 67+i*25, 67+i*25);
        for (var i = 0; i < 45; i++)
        {
            var x = center.X - 126 + i*6;
            var wave = .3 + .7*Math.Pow(Math.Abs(Math.Sin(i*.22)), .7);
            var envelope = Math.Sin(Math.PI*i/44);
            var height = 12+107*wave*envelope;
            var brush = new LinearGradientBrush(Color.FromArgb(205,252,178,151), Color.FromArgb(140,170,112,224), 90);
            dc.DrawRoundedRectangle(brush, null, new Rect(x,center.Y-height/2,3,height), 2, 2);
        }
        for (var i = 0; i < 16; i++)
        {
            var x = 32+(i*79%410); var y = 19+(i*47%165);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(80,227,187,211)), null, new Point(x,y), i%3==0?1.5:.8, i%3==0?1.5:.8);
        }
        dc.Pop();
    }
}

public sealed class AlbumArt : FrameworkElement
{
    public static readonly DependencyProperty SeedProperty = DependencyProperty.Register(nameof(Seed), typeof(string), typeof(AlbumArt), new FrameworkPropertyMetadata("Neon Skyline", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CoverPathProperty = DependencyProperty.Register(nameof(CoverPath), typeof(string), typeof(AlbumArt), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, CoverChanged));
    public string Seed { get => (string)GetValue(SeedProperty); set => SetValue(SeedProperty,value); }
    public string? CoverPath { get => (string?)GetValue(CoverPathProperty); set => SetValue(CoverPathProperty,value); }
    private BitmapImage? cover;
    private static void CoverChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        var art = (AlbumArt)source;
        art.cover = null;
        if (args.NewValue is not string path || !File.Exists(path)) return;
        try
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.DecodePixelWidth=600; image.UriSource=new Uri(path); image.EndInit(); image.Freeze(); art.cover=image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException) { }
    }
    protected override void OnRender(DrawingContext dc)
    {
        var width=ActualWidth; var height=ActualHeight;
        if (width<=0||height<=0) return;
        dc.PushClip(new RectangleGeometry(new Rect(0,0,width,height), 8,8));
        if (cover is not null)
        {
            var scale = Math.Max(width/cover.PixelWidth, height/cover.PixelHeight);
            dc.DrawImage(cover,new Rect((width-cover.PixelWidth*scale)/2,(height-cover.PixelHeight*scale)/2,cover.PixelWidth*scale,cover.PixelHeight*scale)); dc.Pop(); return;
        }
        var seed = Seed ?? "";
        var palette = seed.Contains("Golden",StringComparison.OrdinalIgnoreCase)?1 : seed.Contains("Midnight",StringComparison.OrdinalIgnoreCase)?2 : seed.Contains("Neon",StringComparison.OrdinalIgnoreCase)?0 : Math.Abs(seed.Aggregate(0,(a,c)=>(a*31+c)&0x7fffffff))%3;
        var (dark,accent,light) = palette switch
        {
            1 => (Color.FromRgb(86,54,42),Color.FromRgb(235,153,84),Color.FromRgb(254,219,143)),
            2 => (Color.FromRgb(43,34,88),Color.FromRgb(142,113,229),Color.FromRgb(177,205,250)),
            _ => (Color.FromRgb(71,36,60),Color.FromRgb(212,107,145),Color.FromRgb(255,174,133))
        };
        dc.DrawRectangle(new LinearGradientBrush(dark,accent,135),null,new Rect(0,0,width,height));
        var center=new Point(width*.57,height*.49);var radius=Math.Max(width,height)*.38;
        dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(135,light.R,light.G,light.B),Color.FromArgb(0,light.R,light.G,light.B)),null,center,radius*1.8,radius*1.8);
        for(var i=0;i<27;i++)
        {
            var r=radius*(.45+i*.035);
            var opacity=(byte)(80-(i*2));
            var brush=new SolidColorBrush(Color.FromArgb(opacity,light.R,light.G,light.B));
            dc.DrawEllipse(null,new Pen(brush,Math.Max(.4,width/350)),center,r,r);
        }
        var geometry=new StreamGeometry();
        using(var ctx=geometry.Open())
        {
            ctx.BeginFigure(new Point(-width*.1,height*.89),true,true);
            ctx.BezierTo(new Point(width*.33,-height*.01),new Point(width*.43,height*1.26),new Point(width*1.1,height*.14),true,false);
            ctx.LineTo(new Point(width*1.1,height*1.1),true,false);ctx.LineTo(new Point(-width*.1,height*1.1),true,false);
        }
        geometry.Freeze();
        dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(160,dark.R,dark.G,dark.B),Color.FromArgb(245,dark.R,dark.G,dark.B),90),null,geometry);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(215,light.R,light.G,light.B)),null,new Point(width*.68,height*.35),width*.10,width*.10);
        if(width>90)
        {
            var text=new FormattedText("E N C O R E  O R I G I N A L S",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),Math.Max(7,width*.035),new SolidColorBrush(Color.FromArgb(190,255,244,228)),VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text,new Point(13,height-23));
        }
        dc.Pop();
    }
}
