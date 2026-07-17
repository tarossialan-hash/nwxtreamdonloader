using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.DownloadManager;
using NWXtreamDownloader.Services;

namespace NWXtreamDownloader.ViewModels;

/// <summary>Tela de Configurações.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly DownloadService _downloads;
    private readonly SecurityService _security;

    [ObservableProperty] private string _downloadFolder;
    [ObservableProperty] private string _maxConcurrentText;
    [ObservableProperty] private string _speedLimitText;
    [ObservableProperty] private string _segmentsText;
    [ObservableProperty] private string _language;
    [ObservableProperty] private string _userAgent;
    [ObservableProperty] private string _seasonPattern;
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private bool _saveCredentials;
    [ObservableProperty] private bool _overwriteExisting;
    [ObservableProperty] private string _statusMessage = string.Empty;



    public string[] Languages { get; } = ["Português", "English"];

    // Segurança
    [ObservableProperty] private string _currentPassword = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _securityStatus = string.Empty;
    [ObservableProperty] private string _lastPasswordChange = string.Empty;

    public SettingsViewModel(SettingsService settings, DownloadService downloads, SecurityService security)
    {
        _settings = settings;
        _downloads = downloads;
        _security = security;
        _lastPasswordChange = $"Última alteração de senha: {security.LastChanged}";
        _downloadFolder = settings.DownloadFolder;
        _maxConcurrentText = settings.MaxConcurrent.ToString();
        _speedLimitText = settings.SpeedLimitKB.ToString();
        _segmentsText = settings.SegmentsPerDownload.ToString();
        _language = settings.Language;
        _userAgent = settings.UserAgent;
        _seasonPattern = settings.SeasonFolderPattern;
        _isDarkTheme = settings.Theme == "Dark";
        _saveCredentials = settings.SaveCredentials;
        _overwriteExisting = settings.OverwriteExisting;
    }

    /// <summary>Aplica e GRAVA a pasta de download na hora (chamado pelo botão Procurar).</summary>
    public void ApplyDownloadFolder(string path)
    {
        DownloadFolder = path;
        _settings.DownloadFolder = path;
        StatusMessage = $"Pasta de download atualizada: {path}";
    }

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(MaxConcurrentText, out var max) || max < 1 || max > 10)
        {
            StatusMessage = "Downloads simultâneos deve ser um número de 1 a 10.";
            return;
        }
        if (!int.TryParse(SpeedLimitText, out var limit) || limit < 0)
        {
            StatusMessage = "Limite de velocidade inválido (use 0 para ilimitado).";
            return;
        }
        if (!int.TryParse(SegmentsText, out var segments) || segments < 1 || segments > 8)
        {
            StatusMessage = "Conexões por download deve ser de 1 a 8.";
            return;
        }
        _settings.DownloadFolder = DownloadFolder.Trim();
        _settings.MaxConcurrent = max;
        _settings.SpeedLimitKB = limit;
        _settings.Language = Language;
        _settings.UserAgent = UserAgent;
        UserAgent = _settings.UserAgent;
        _settings.SeasonFolderPattern = SeasonPattern;
        SeasonPattern = _settings.SeasonFolderPattern;
        _settings.SaveCredentials = SaveCredentials;
        _settings.OverwriteExisting = OverwriteExisting;
        _settings.Theme = IsDarkTheme ? "Dark" : "Light"; // dispara ThemeChanged

        _settings.SegmentsPerDownload = segments;

        // aplica imediatamente no gerenciador de downloads
        _downloads.MaxConcurrent = max;
        _downloads.SpeedLimitKB = limit;
        _downloads.SegmentsPerDownload = segments;

        StatusMessage = "Configurações salvas.";
    }

    /// <summary>Alterar senha (seção Segurança): exige a senha atual e confirmação da nova.</summary>
    [RelayCommand]
    private void ChangePassword()
    {
        if (!_security.Verify(CurrentPassword))
        {
            SecurityStatus = "✖ Senha atual incorreta.";
            return;
        }
        if (NewPassword.Length < 6)
        {
            SecurityStatus = "A nova senha precisa ter pelo menos 6 caracteres.";
            return;
        }
        if (NewPassword == SecurityService.DefaultPassword)
        {
            SecurityStatus = "A nova senha não pode ser a senha padrão.";
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            SecurityStatus = "As senhas não conferem.";
            return;
        }
        _security.SetPassword(NewPassword);
        CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
        LastPasswordChange = $"Última alteração de senha: {_security.LastChanged}";
        SecurityStatus = "✔ Senha alterada com sucesso.";
    }
}
