using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.Services;

namespace NWXtreamDownloader.ViewModels;

/// <summary>Aba Novos Conteúdos: controle do que foi adicionado nesta sessão.</summary>
public partial class NewContentViewModel : ViewModelBase
{
    public NewContentLog Log { get; }

    public NewContentViewModel(NewContentLog log) => Log = log;

    public string ToText() => Log.ToText();

    [RelayCommand]
    private void Clear() => Log.Clear();
}
