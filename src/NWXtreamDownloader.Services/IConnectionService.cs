using NWXtreamDownloader.Models;

namespace NWXtreamDownloader.Services;

/// <summary>Gerencia a conexão com o servidor Xtream e as credenciais salvas.</summary>
public interface IConnectionService
{
    XtreamAccountInfo? AccountInfo { get; }
    bool IsConnected { get; }

    /// <summary>Valida as credenciais no servidor e, se ok, salva criptografadas.</summary>
    Task<XtreamAccountInfo> ConnectAsync(XtreamCredentials credentials, bool saveCredentials, CancellationToken ct = default);

    /// <summary>Só testa as credenciais no servidor, sem salvar nada.</summary>
    Task<XtreamAccountInfo> TestAsync(XtreamCredentials credentials, CancellationToken ct = default);

    /// <summary>Credenciais salvas (para preencher os campos), ou null.</summary>
    XtreamCredentials? GetSavedCredentials();

    /// <summary>Tenta reconectar usando credenciais salvas. Retorna false se não houver ou falhar.</summary>
    Task<bool> TryAutoConnectAsync(CancellationToken ct = default);

    /// <summary>Remove as credenciais salvas e desconecta.</summary>
    void Disconnect();
}
