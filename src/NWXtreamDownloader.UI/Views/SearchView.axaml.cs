using Avalonia.Controls;
using Avalonia.Interactivity;
using NWXtreamDownloader.ViewModels;

namespace NWXtreamDownloader.UI.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
    }

    private void OnDownload(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is SearchResult result &&
            DataContext is SearchViewModel vm)
            vm.DownloadCommand.Execute(result);
    }
}
