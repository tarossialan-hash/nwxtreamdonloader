using Avalonia.Controls;
using Avalonia.Interactivity;
using NWXtreamDownloader.Models;
using NWXtreamDownloader.ViewModels;

namespace NWXtreamDownloader.UI.Views;

public partial class SeriesView : UserControl
{
    public SeriesView()
    {
        InitializeComponent();
    }

    // Handlers no code-behind: bindings com cast de tipo dentro de DataTemplates
    // são resolvidos em runtime pelo Avalonia e derrubavam o app.

    private void OnDownloadSeason(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SeasonGroup season } && DataContext is SeriesViewModel vm)
            vm.DownloadSeasonCommand.Execute(season);
    }

    private void OnDownloadEpisode(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Episode episode } && DataContext is SeriesViewModel vm)
            vm.DownloadEpisodeCommand.Execute(episode);
    }
}
