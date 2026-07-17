using System.Text.Json;
using NWXtreamDownloader.Api;
using NWXtreamDownloader.Helpers;
using NWXtreamDownloader.Models;

namespace NWXtreamDownloader.Services;

/// <summary>
/// Conecta ao servidor Xtream via <see cref="XtreamApiClient"/> e persiste
/// as credenciais criptografadas com DPAPI em %AppData%\NWXtreamDownloader.
/// </summary>
public class ConnectionService : IConnectionService
{
    private readonly XtreamApiClient _api;

    public ConnectionService(XtreamApiClient api) => _api = api;

    public XtreamAccountInfo? AccountInfo { get; private set; }
    public bool IsConnected => AccountInfo?.Authenticated == true;

    public async Task<XtreamAccountInfo> ConnectAsync(XtreamCredentials credentials, bool saveCredentials, CancellationToken ct = default)
    {
        var info = await _api.AuthenticateAsync(credentials, ct);
        if (!info.Authenticated)
            throw new UnauthorizedAccessException("Usuário ou senha inválidos.");
        if (!string.IsNullOrEmpty(info.Status) && !info.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Conta não está ativa (status: {info.Status}).");

        AccountInfo = info;
        if (saveCredentials)
            Save(credentials);
        return info;
    }

    public async Task<XtreamAccountInfo> TestAsync(XtreamCredentials credentials, CancellationToken ct = default)
    {
        var info = await _api.AuthenticateAsync(credentials, ct);
        if (!info.Authenticated)
            throw new UnauthorizedAccessException("Usuário ou senha inválidos.");
        return info;
    }

    public XtreamCredentials? GetSavedCredentials() => Load();

    public async Task<bool> TryAutoConnectAsync(CancellationToken ct = default)
    {
        var credentials = Load();
        if (credentials is null)
            return false;
        try
        {
            await ConnectAsync(credentials, saveCredentials: false, ct);
            return true;
        }
        catch
        {
            return false; // servidor fora do ar ou credenciais expiradas: cai na tela de login
        }
    }

    public void Disconnect()
    {
        AccountInfo = null;
        if (File.Exists(AppPaths.CredentialsFile))
            File.Delete(AppPaths.CredentialsFile);
    }

    private static void Save(XtreamCredentials c) =>
        File.WriteAllBytes(AppPaths.CredentialsFile, CryptoHelper.Protect(JsonSerializer.Serialize(c)));

    private static XtreamCredentials? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.CredentialsFile))
                return null;
            var json = CryptoHelper.Unprotect(File.ReadAllBytes(AppPaths.CredentialsFile));
            return JsonSerializer.Deserialize<XtreamCredentials>(json);
        }
        catch
        {
            return null; // arquivo corrompido: ignora e pede login novamente
        }
    }
}
