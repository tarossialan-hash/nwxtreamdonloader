using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using NWXtreamDownloader.Services;
using NWXtreamDownloader.UI.Views;

namespace NWXtreamDownloader.UI;

/// <summary>Abre o diálogo de escolha de pasta remota sobre a janela principal.</summary>
public class RemoteFolderPicker : IRemoteFolderPicker
{
    private readonly SftpUploadService _sftp;
    private readonly SettingsService _settings;

    public RemoteFolderPicker(SftpUploadService sftp, SettingsService settings)
    {
        _sftp = sftp;
        _settings = settings;
    }

    public async Task<string?> PickAsync()
    {
        // SFTP desligado: baixa normalmente, sem popup
        if (!_settings.SftpEnabled)
            return "";
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
            return "";

        var dialog = new RemoteFolderPickerWindow(_sftp, _settings.SftpRemoteDir);
        var confirmed = await dialog.ShowDialog<bool>(desktop.MainWindow);
        if (!confirmed)
            return null;

        // a pasta escolhida vira o ponto de partida do próximo popup
        if (!string.IsNullOrWhiteSpace(dialog.SelectedPath))
            _settings.SftpRemoteDir = dialog.SelectedPath;
        return dialog.SelectedPath;
    }
}
