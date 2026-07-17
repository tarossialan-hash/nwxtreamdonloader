using Avalonia.Controls;
using Avalonia.Interactivity;
using NWXtreamDownloader.ViewModels;

namespace NWXtreamDownloader.UI.Views;

public partial class NewContentView : UserControl
{
    public NewContentView()
    {
        InitializeComponent();
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NewContentViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(vm.ToText());
    }
}
