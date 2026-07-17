using Avalonia;
using System;
using System.Threading.Tasks;
using NWXtreamDownloader.Helpers;

namespace NWXtreamDownloader.UI;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Qualquer erro não tratado fica registrado em %AppData%\NWXtreamDownloader\app.log
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Error("UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Error("UnobservedTask", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
