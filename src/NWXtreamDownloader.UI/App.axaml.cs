using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using NWXtreamDownloader.Api;
using NWXtreamDownloader.Database;
using NWXtreamDownloader.DownloadManager;
using NWXtreamDownloader.Services;
using NWXtreamDownloader.UI.Views;
using NWXtreamDownloader.ViewModels;

namespace NWXtreamDownloader.UI;

public partial class App : Application
{
    /// <summary>Container de injeção de dependência da aplicação.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        // banco + estado inicial
        var db = Services.GetRequiredService<DatabaseService>();
        db.Init();

        var settings = Services.GetRequiredService<SettingsService>();
        ApplyTheme(settings.Theme);
        settings.ThemeChanged += ApplyTheme;

        // User-Agent em todas as requisições (login, listas e downloads)
        var http = Services.GetRequiredService<HttpClient>();
        ApplyUserAgent(http, settings.UserAgent);
        settings.UserAgentChanged += ua => ApplyUserAgent(http, ua);

        var downloads = Services.GetRequiredService<DownloadService>();
        downloads.UiInvoke = action => Dispatcher.UIThread.Post(action);
        downloads.MaxConcurrent = settings.MaxConcurrent;
        downloads.SpeedLimitKB = settings.SpeedLimitKB;
        downloads.SegmentsPerDownload = settings.SegmentsPerDownload;
        downloads.LoadPending(); // restaura fila pendente do banco

        // envio automático via SFTP ao concluir cada download
        var sftp = Services.GetRequiredService<SftpUploadService>();
        sftp.UiInvoke = action => Dispatcher.UIThread.Post(action);
        sftp.MaxUploads = settings.UploadConcurrency;
        Services.GetRequiredService<NewContentLog>().UiInvoke = action => Dispatcher.UIThread.Post(action);
        downloads.Completed += item => _ = sftp.UploadAsync(item);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<ShellViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(string theme) =>
        RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private static void ApplyUserAgent(HttpClient http, string userAgent)
    {
        http.DefaultRequestHeaders.Remove("User-Agent");
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // HttpClient único reutilizado por toda a aplicação
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(100) });
        services.AddSingleton<XtreamApiClient>();
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<DownloadService>();
        services.AddSingleton<NewContentLog>();
        services.AddSingleton<MediaDownloadService>();
        services.AddSingleton<SftpUploadService>();
        services.AddSingleton<CatalogService>();
        services.AddSingleton<SecurityService>();
        services.AddSingleton<IRemoteFolderPicker, RemoteFolderPicker>();
        services.AddSingleton<IConnectionService, ConnectionService>();

        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<MoviesViewModel>();
        services.AddSingleton<SeriesViewModel>();
        services.AddSingleton<DownloadsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SftpViewModel>();
        services.AddSingleton<PlaylistsViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<NewContentViewModel>();
        services.AddSingleton<LockViewModel>();
        services.AddSingleton<ShellViewModel>();
    }
}
