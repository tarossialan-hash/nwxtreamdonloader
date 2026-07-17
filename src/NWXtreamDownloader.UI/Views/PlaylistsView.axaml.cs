using Avalonia.Controls;
using Avalonia.Interactivity;
using NWXtreamDownloader.Models;
using NWXtreamDownloader.ViewModels;

namespace NWXtreamDownloader.UI.Views;

public partial class PlaylistsView : UserControl
{
    public PlaylistsView()
    {
        InitializeComponent();
    }

    private PlaylistsViewModel? Vm => DataContext as PlaylistsViewModel;

    private static Playlist? ItemOf(object? sender) =>
        (sender as Button)?.DataContext as Playlist;

    private void OnSync(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } p && Vm is { } vm) _ = vm.SyncAsync(p);
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } p) Vm?.ToggleActive(p);
    }

    private void OnEdit(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } p) Vm?.BeginEdit(p);
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } p) Vm?.Delete(p);
    }
}
