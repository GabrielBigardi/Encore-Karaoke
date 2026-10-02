using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Encore.Diagnostics;
using Encore.Services;

namespace Encore;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? Option(string name)=>e.Args.FirstOrDefault(a=>a.StartsWith(name+"=",StringComparison.OrdinalIgnoreCase))?[(name.Length+1)..];
        try
        {
            if(e.Args.Contains("--self-test"))
            {
                var report=SelfTests.Run();
                var destination=Option("--report")??Path.Combine(AppContext.BaseDirectory,"self-test.json");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
                File.WriteAllText(destination,System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                Shutdown(report.Failed==0?0:1);return;
            }
            var dataFolder=Option("--data-dir");
            if((e.Args.Contains("--ui-test")||e.Args.Contains("--render-test"))&&dataFolder is null)dataFolder=Path.Combine(Path.GetTempPath(),"Encore-ui-test-"+Guid.NewGuid().ToString("N"));
            var storage=new AppStorage(dataFolder);
            storage.InstallDemos();
            if(e.Args.Contains("--audio-test"))
            {
                var report=await AudioTests.Run(storage);
                File.WriteAllText(Option("--report")??Path.Combine(AppContext.BaseDirectory,"audio-test.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                Shutdown(report.Passed?0:1);return;
            }
            DispatcherUnhandledException+=(_,args)=>
            {
                var diagnostic=Option("--error-log");
                if(diagnostic is not null){File.WriteAllText(diagnostic,args.Exception.ToString());args.Handled=true;Shutdown(1);return;}
                if(args.Exception is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show(args.Exception.Message,"Encore",MessageBoxButton.OK,MessageBoxImage.Information);args.Handled=true;
                }
            };
            var window=new MainWindow(storage);MainWindow=window;
            var screenshot=Option("--screenshot");
            if(screenshot is not null)
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                window.Width=1360;window.Height=900;
                // Render offscreen for verification, without opening a visible helper window.
                window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-20000;window.Top=-20000;window.ShowInTaskbar=false;
                window.Show();
                await Task.Delay(1600);
                if(Option("--size")=="compact"){window.Width=1080;window.Height=720;window.UpdateLayout();}
                if(Option("--size")=="small"){window.Width=960;window.Height=600;window.UpdateLayout();}
                window.PrepareScreenshot(Option("--view")??"library");
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);
                window.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
                bitmap.Render(window);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
                using(var file=File.Create(screenshot)){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(file);}
                window.Close();Shutdown();return;
            }
            if(e.Args.Contains("--render-test"))
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                window.ShowInTaskbar=false;window.ShowActivated=false;window.Opacity=e.Args.Contains("--render-visible")?1:0;window.Show();
                window.WindowState=WindowState.Maximized;
                await Task.Delay(1100);
                var report=await window.RunRenderingTest();
                File.WriteAllText(Option("--report")??Path.Combine(AppContext.BaseDirectory,"render-test.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                window.Close();Shutdown(report.Passed?0:1);return;
            }
            if(e.Args.Contains("--ui-test"))
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-20000;window.Top=-20000;window.ShowInTaskbar=false;window.ShowActivated=false;window.Opacity=0;window.Show();
                await Task.Delay(1100);
                var report=await window.RunInteractionTests();
                File.WriteAllText(Option("--report")??Path.Combine(AppContext.BaseDirectory,"ui-test.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                window.Close();Shutdown(report.Failed==0?0:1);return;
            }
            window.Show();
        }
        catch(Exception ex)
        {
            var errorPath=Option("--error-log");
            if(errorPath is not null)File.WriteAllText(errorPath,ex.ToString());
            else MessageBox.Show("Encore couldn't start.\n\n"+ex.Message,"Encore",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
