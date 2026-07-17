using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.Models;
using NWXtreamDownloader.Services;

namespace NWXtreamDownloader.ViewModels;

/// <summary>
/// Barra de conexão exibida no topo do app: Servidor/Usuário/Senha,
/// Conectar, Testar conexão e status. Campos são preenchidos com as
/// credenciais salvas e a reconexão é automática na abertura.
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly IConnectionService _connection;
    private readonly Database.DatabaseService _db;
    private readonly CatalogService _catalog;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private string _m3uUrl = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusMessage = "Desconectado";

    [ObservableProperty]
    private bool _saveCredentials = true;

    /// <summary>Disparado quando a conexão é validada com sucesso.</summary>
    public event EventHandler? LoginSucceeded;

    public LoginViewModel(IConnectionService connection, SettingsService settings,
        Database.DatabaseService db, CatalogService catalog)
    {
        _connection = connection;
        _db = db;
        _catalog = catalog;
        _saveCredentials = settings.SaveCredentials;

        // preenche os campos com as credenciais salvas (criptografadas)
        var saved = connection.GetSavedCredentials();
        if (saved is not null)
        {
            _m3uUrl = $"{saved.Server}/get.php?username={saved.Username}&password={saved.Password}&type=m3u_plus&output=ts";
        }
    }

    /// <summary>Reconexão automática na abertura do app.</summary>
    public async Task TryAutoConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(M3uUrl)) return;
        IsBusy = true;
        StatusMessage = "Reconectando...";
        try
        {
            if (await _connection.TryAutoConnectAsync())
            {
                IsConnected = true;
                StatusMessage = BuildConnectedMessage();
                LoginSucceeded?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                StatusMessage = "Desconectado — verifique os dados e clique em Conectar.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(M3uUrl) &&
        M3uUrl.Contains("username=", StringComparison.OrdinalIgnoreCase) &&
        M3uUrl.Contains("password=", StringComparison.OrdinalIgnoreCase);

    private XtreamCredentials BuildCredentials() => XtreamCredentials.FromM3uUrl(M3uUrl);

    /// <summary>
    /// Conectou pela barra: garante a lista no cadastro (aba Listas) e invalida
    /// o cache do catálogo — lista trocada é recarregada na hora.
    /// </summary>
    private void RegisterAsPlaylist()
    {
        var url = M3uUrl.Trim();
        var credentials = XtreamCredentials.FromM3uUrl(url);

        // procura pela mesma conta (servidor + usuário), não pela URL exata —
        // evita listas duplicadas quando só um parâmetro da URL muda
        var existing = _db.GetPlaylists().FirstOrDefault(p =>
        {
            try
            {
                var c = XtreamCredentials.FromM3uUrl(p.Url);
                return c.Server == credentials.Server && c.Username == credentials.Username;
            }
            catch { return false; }
        });

        if (existing is null)
        {
            var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "Minha Lista";
            _db.InsertPlaylist(host, url);
        }
        else if (existing.Url != url)
        {
            existing.Url = url;
            _db.UpdatePlaylist(existing);
        }
        _catalog.Invalidate(); // dados antigos nunca ficam na tela
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        StatusMessage = "Conectando...";
        try
        {
            await _connection.ConnectAsync(BuildCredentials(), SaveCredentials);
            RegisterAsPlaylist();
            IsConnected = true;
            StatusMessage = BuildConnectedMessage();
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (UnauthorizedAccessException ex)
        {
            IsConnected = false;
            StatusMessage = ex.Message;
        }
        catch (Exception)
        {
            IsConnected = false;
            StatusMessage = "Não foi possível conectar. Verifique o endereço e sua internet.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task TestAsync()
    {
        IsBusy = true;
        StatusMessage = "Testando conexão...";
        try
        {
            var info = await _connection.TestAsync(BuildCredentials());
            var exp = info.ExpiresAt?.ToString("dd/MM/yyyy") ?? "—";
            StatusMessage = $"✔ Conexão OK — status: {info.Status}, expira em {exp}, {info.MaxConnections} conexão(ões).";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = $"✖ {ex.Message}";
        }
        catch (Exception)
        {
            StatusMessage = "✖ Servidor inacessível. Verifique o endereço e sua internet.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string BuildConnectedMessage()
    {
        var info = _connection.AccountInfo;
        var exp = info?.ExpiresAt?.ToString("dd/MM/yyyy");
        return exp is null ? "Conectado" : $"Conectado — expira em {exp}";
    }
}
