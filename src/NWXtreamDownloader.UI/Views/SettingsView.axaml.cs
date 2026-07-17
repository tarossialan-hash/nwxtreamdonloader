using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NWXtreamDownloader.ViewModels;

namespace NWXtreamDownloader.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>Abre o seletor de pasta do Windows para escolher onde salvar os downloads.</summary>
    private async void OnPickFolder(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage ||
            DataContext is not SettingsViewModel vm)
            return;

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Escolha a pasta de downloads",
            AllowMultiple = false,
        });
        if (result.Count > 0)
            vm.ApplyDownloadFolder(result[0].Path.LocalPath); // grava imediatamente
    }
}
