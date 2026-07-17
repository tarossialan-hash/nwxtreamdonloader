using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.Services;

namespace NWXtreamDownloader.ViewModels;

/// <summary>Tela de Configurações SFTP.</summary>
public partial class SftpViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly SftpUploadService _sftp;

    [ObservableProperty] private bool _sftpEnabled;
    [ObservableProperty] private string _sftpHost;
    [ObservableProperty] private string _sftpPortText;
    [ObservableProperty] private string _sftpUser;
    [ObservableProperty] private string _sftpPassword;
    [ObservableProperty] private string _sftpRemoteDir;
    [ObservableProperty] private string _uploadConcurrencyText;
    [ObservableProperty] private string _sftpStatus = string.Empty;
    [ObservableProperty] private bool _isTestingSftp;
    [ObservableProperty] private bool _isConnected;

    public System.Collections.ObjectModel.ObservableCollection<string> RemoteFolders { get; } = [];

    [ObservableProperty] private string? _selectedRemoteFolder;

    public SftpViewModel(SettingsService settings, SftpUploadService sftp)
    {
        _settings = settings;
        _sftp = sftp;

        _sftpEnabled = settings.SftpEnabled;
        _sftpHost = settings.SftpHost;
        _sftpPortText = settings.SftpPort.ToString();
        _sftpUser = settings.SftpUser;
        _sftpPassword = settings.SftpPassword;
        _sftpRemoteDir = settings.SftpRemoteDir;
        _uploadConcurrencyText = settings.UploadConcurrency.ToString();
    }

    /// <summary>Pastas criadas fora do app (FileZilla etc.) passam a aparecer no popup.</summary>
    [RelayCommand]
    private void RefreshFolders()
    {
        _sftp.ClearDirCache();
        SftpStatus = "Pastas atualizadas — o popup de download vai recarregar direto do servidor.";
    }

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(SftpPortText, out var port) || port < 1 || port > 65535)
        {
            SftpStatus = "Porta SFTP inválida.";
            return;
        }

        _settings.SftpEnabled = SftpEnabled;
        _settings.SftpHost = SftpHost;
        _settings.SftpPort = port;
        _settings.SftpUser = SftpUser;
        _settings.SftpPassword = SftpPassword;
        _settings.SftpRemoteDir = SftpRemoteDir;

        if (!int.TryParse(UploadConcurrencyText, out var uploads) || uploads < 1 || uploads > 4)
        {
            SftpStatus = "Envios simultâneos deve ser de 1 a 4.";
            return;
        }
        _settings.UploadConcurrency = uploads;
        _sftp.MaxUploads = uploads;

        SftpStatus = "Configurações SFTP salvas.";
    }

    partial void OnSftpEnabledChanged(bool value)
    {
        _settings.SftpEnabled = value;
    }

    private bool PersistSftpConnection()
    {
        if (string.IsNullOrWhiteSpace(SftpHost) || string.IsNullOrWhiteSpace(SftpUser) ||
            !int.TryParse(SftpPortText, out var port) || port < 1 || port > 65535)
        {
            SftpStatus = "Preencha host, porta e usuário do SFTP.";
            return false;
        }
        _settings.SftpHost = SftpHost;
        _settings.SftpPort = port;
        _settings.SftpUser = SftpUser;
        _settings.SftpPassword = SftpPassword;
        _settings.SftpRemoteDir = SftpRemoteDir;
        return true;
    }

    [RelayCommand]
    private async Task BrowseRemoteAsync()
    {
        if (!PersistSftpConnection()) return;
        SftpStatus = "Listando pastas remotas...";
        try
        {
            var dirs = await _sftp.ListDirectoriesAsync(SftpRemoteDir);
            RemoteFolders.Clear();
            RemoteFolders.Add("‹ voltar");
            foreach (var d in dirs)
                RemoteFolders.Add(d);
            SftpStatus = $"{dirs.Count} pasta(s) em {SftpRemoteDir} — clique para entrar; a pasta atual é a de envio.";
        }
        catch (System.Exception ex)
        {
            SftpStatus = $"✖ {ex.Message}";
        }
    }

    partial void OnSelectedRemoteFolderChanged(string? value)
    {
        if (value is null) return;
        SelectedRemoteFolder = null;
        if (value == "‹ voltar")
        {
            var p = SftpRemoteDir.TrimEnd('/');
            var i = p.LastIndexOf('/');
            SftpRemoteDir = i <= 0 ? "/" : p[..i];
        }
        else
        {
            SftpRemoteDir = value;
        }
        _ = BrowseRemoteAsync();
    }

    [ObservableProperty] private string _newFolderName = string.Empty;

    /// <summary>Cria uma subpasta dentro da pasta remota atual.</summary>
    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        var name = NewFolderName.Trim();
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\'))
        {
            SftpStatus = "Digite um nome de pasta válido (sem / ou \\).";
            return;
        }
        if (!PersistSftpConnection()) return;
        SftpStatus = $"Criando pasta \"{name}\"...";
        try
        {
            await _sftp.CreateDirectoryAsync(SftpRemoteDir.TrimEnd('/') + "/" + name);
            NewFolderName = string.Empty;
            await BrowseRemoteAsync();
        }
        catch (System.Exception ex)
        {
            SftpStatus = $"✖ Não foi possível criar: {ex.Message}";
        }
    }

    private string? _pendingDelete;

    /// <summary>
    /// Exclui a pasta remota atual COM todo o conteúdo. Exige dois cliques:
    /// o primeiro pede confirmação, o segundo executa.
    /// </summary>
    [RelayCommand]
    private async Task DeleteCurrentFolderAsync()
    {
        var target = SftpRemoteDir.TrimEnd('/');
        if (target.Length <= 1 || target.LastIndexOf('/') <= 0)
        {
            SftpStatus = "Não é possível excluir a raiz do servidor.";
            return;
        }

        // 1º clique: só avisa e arma a confirmação
        if (_pendingDelete != target)
        {
            _pendingDelete = target;
            SftpStatus = $"⚠ \"{target}\" e TODO o conteúdo dentro serão apagados do servidor. Clique em Excluir de novo para confirmar.";
            return;
        }

        _pendingDelete = null;
        if (!PersistSftpConnection()) return;
        SftpStatus = "Excluindo pasta e conteúdo...";
        try
        {
            await _sftp.DeleteDirectoryAsync(target);
            SftpRemoteDir = target[..target.LastIndexOf('/')];
            _settings.SftpRemoteDir = SftpRemoteDir;
            await BrowseRemoteAsync();
        }
        catch (System.Exception ex)
        {
            SftpStatus = $"✖ Não foi possível excluir: {ex.Message}";
        }
    }

    private bool _monitorStarted;

    /// <summary>
    /// Monitor do indicador: conecta sozinho na abertura (se configurado) e
    /// verifica a conexão a cada 45s — verde conectado, vermelho caiu.
    /// Chamar a partir da thread de UI (Shell).
    /// </summary>
    public async Task StartMonitorAsync()
    {
        if (_monitorStarted) return;
        _monitorStarted = true;
        while (true)
        {
            if (!string.IsNullOrWhiteSpace(_settings.SftpHost))
                IsConnected = await _sftp.PingAsync();
            await Task.Delay(System.TimeSpan.FromSeconds(45));
        }
    }

    [RelayCommand]
    private async Task TestSftpAsync()
    {
        Save();
        if (SftpStatus != "Configurações SFTP salvas.") return;

        IsTestingSftp = true;
        SftpStatus = "Conectando ao SFTP...";
        IsConnected = false;
        try
        {
            SftpStatus = await _sftp.TestAsync();
            if (SftpStatus.StartsWith("✔ SFTP OK"))
            {
                IsConnected = true;
                if (!SftpEnabled)
                    SftpStatus += " ⚠ ATENÇÃO: o envio automático está DESATIVADO — ligue o interruptor acima.";
            }
        }
        finally
        {
            IsTestingSftp = false;
        }
    }
}
