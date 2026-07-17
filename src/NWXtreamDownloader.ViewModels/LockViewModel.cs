using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.Services;

namespace NWXtreamDownloader.ViewModels;

/// <summary>
/// Tela de bloqueio: pede a senha ao abrir o app. No primeiro acesso
/// (senha padrão) obriga a definir uma senha nova antes de continuar.
/// </summary>
public partial class LockViewModel : ViewModelBase
{
    private readonly SecurityService _security;

    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _mustChange;

    /// <summary>Disparado quando a senha é validada (Shell libera o app).</summary>
    public event Action? Unlocked;

    public LockViewModel(SecurityService security)
    {
        _security = security;
        if (security.IsFirstAccess)
            Message = $"Primeiro acesso: use a senha padrão \"{SecurityService.DefaultPassword}\".";
    }

    [RelayCommand]
    private void Enter()
    {
        if (!MustChange)
        {
            if (!_security.Verify(Password))
            {
                Message = "✖ Senha incorreta.";
                return;
            }
            if (_security.IsFirstAccess)
            {
                MustChange = true;
                Message = "Por segurança, altere sua senha antes de continuar.";
                return;
            }
            Unlock();
        }
        else
        {
            if (NewPassword.Length < 6)
            {
                Message = "A nova senha precisa ter pelo menos 6 caracteres.";
                return;
            }
            if (NewPassword == SecurityService.DefaultPassword)
            {
                Message = "A nova senha não pode ser a senha padrão.";
                return;
            }
            if (NewPassword != ConfirmPassword)
            {
                Message = "As senhas não conferem.";
                return;
            }
            _security.SetPassword(NewPassword);
            Unlock();
        }
    }

    private void Unlock()
    {
        Password = NewPassword = ConfirmPassword = string.Empty;
        Message = string.Empty;
        Unlocked?.Invoke();
    }
}
