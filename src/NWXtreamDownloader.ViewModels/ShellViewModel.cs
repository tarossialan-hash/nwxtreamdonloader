using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NWXtreamDownloader.ViewModels;

/// <summary>Item do menu lateral.</summary>
public partial class NavItem : ObservableObject
{
    public string Icon { get; }
    public string Title { get; }
    
    [ObservableProperty]
    private string _iconColor = "#E4E4EC";

    public NavItem(string icon, string title)
    {
        Icon = icon;
        Title = title;
    }
}

/// <summary>
/// Shell principal pós-login: menu lateral (Filmes, Séries, Downloads, Histórico,
/// Configurações) e área de conteúdo.
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    public LoginViewModel Connection { get; }
    public MoviesViewModel Movies { get; }
    public SeriesViewModel Series { get; }
    public DownloadsViewModel Downloads { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }
    public SftpViewModel Sftp { get; }
    public PlaylistsViewModel Playlists { get; }
    public SearchViewModel Search { get; }
    public NewContentViewModel NewContent { get; }
    public LockViewModel Lock { get; }

    /// <summary>true até o usuário digitar a senha (tela de bloqueio cobre o app).</summary>
    [ObservableProperty]
    private bool _isLocked = true;

    public NavItem[] NavItems { get; } =
    [
        new("\uE714", "Filmes"),
        new("\uE7F4", "Séries"),
        new("", "Pesquisar"),
        new("", "Listas"),
        new("\uE896", "Downloads"),
        new("\uE81C", "Histórico"),
        new("", "Novos Conteúdos"),
        new("\uEC27", "Servidor FTP"),
        new("\uE713", "Configurações"),
    ];

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private ViewModelBase _currentSection;

    public ShellViewModel(LoginViewModel connection, MoviesViewModel movies, SeriesViewModel series,
        DownloadsViewModel downloads, HistoryViewModel history, SettingsViewModel settings, SftpViewModel sftp,
        PlaylistsViewModel playlists, SearchViewModel search, LockViewModel @lock, NewContentViewModel newContent)
    {
        Playlists = playlists;
        Search = search;
        NewContent = newContent;
        Lock = @lock;
        @lock.Unlocked += () => IsLocked = false;
        playlists.ListsChanged += () =>
        {
            _ = movies.LoadAsync();
            _ = series.LoadAsync();
        };
        Connection = connection;
        Movies = movies;
        Series = series;
        Downloads = downloads;
        History = history;
        Settings = settings;
        Sftp = sftp;
        _currentSection = movies;
        _selectedNavItem = NavItems[0];

        // botão "Adicionar lista" dos seletores leva à aba Listas
        System.Action goToLists = () => { SelectedNavItem = NavItems.First(n => n.Title == "Listas"); };
        movies.AddListRequested += goToLists;
        series.AddListRequested += goToLists;

        _ = movies.LoadAsync(); // mostra o seletor de listas já na abertura
        _ = sftp.StartMonitorAsync(); // indicador do FTP verde/vermelho em tempo real

        sftp.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SftpViewModel.IsConnected))
            {
                var sftpItem = NavItems.FirstOrDefault(x => x.Title == "Servidor FTP");
                if (sftpItem != null)
                {
                    sftpItem.IconColor = sftp.IsConnected ? "#4CD964" : "#FF3B30";
                }
            }
        };
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        switch (value?.Title)
        {
            case "Séries":
                CurrentSection = Series;
                _ = Series.LoadAsync();
                break;
            case "Pesquisar":
                CurrentSection = Search;
                break;
            case "Listas":
                Playlists.Refresh();
                CurrentSection = Playlists;
                break;
            case "Downloads":
                CurrentSection = Downloads;
                break;
            case "Novos Conteúdos":
                CurrentSection = NewContent;
                break;
            case "Histórico":
                History.Refresh();
                CurrentSection = History;
                break;
            case "Servidor FTP":
                CurrentSection = Sftp;
                break;
            case "Configurações":
                CurrentSection = Settings;
                break;
            default:
                CurrentSection = Movies;
                _ = Movies.LoadAsync();
                break;
        }
    }
}
