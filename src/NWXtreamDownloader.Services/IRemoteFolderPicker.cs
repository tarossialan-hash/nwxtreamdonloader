namespace NWXtreamDownloader.Services;

/// <summary>
/// Popup para escolher a pasta do servidor FTP na hora do download.
/// Implementado na camada de UI (janela de diálogo).
/// </summary>
public interface IRemoteFolderPicker
{
    /// <summary>
    /// null = usuário cancelou (não baixar);
    /// "" = SFTP desativado (baixa sem escolher pasta);
    /// caminho = pasta remota escolhida para o envio.
    /// </summary>
    Task<string?> PickAsync();
}
