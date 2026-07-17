using Avalonia.Controls;
using Avalonia.Interactivity;
using NWXtreamDownloader.DownloadManager;
using NWXtreamDownloader.ViewModels;

namespace NWXtreamDownloader.UI.Views;

public partial class DownloadsView : UserControl
{
    public DownloadsView()
    {
        InitializeComponent();
    }

    private DownloadsViewModel? Vm => DataContext as DownloadsViewModel;

    private static DownloadItem? ItemOf(object? sender) =>
        (sender as Button)?.DataContext as DownloadItem;

    private void OnPause(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.PauseCommand.Execute(item);
    }

    private void OnResume(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.ResumeCommand.Execute(item);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.CancelCommand.Execute(item);
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.RemoveCommand.Execute(item);
    }

    private void OnForceUpload(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Vm?.ForceUploadCommand.Execute(item);
    }
}
