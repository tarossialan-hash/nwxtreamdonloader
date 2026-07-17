using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.Api;
using NWXtreamDownloader.Helpers;
using NWXtreamDownloader.Models;
using NWXtreamDownloader.Services;

namespace NWXtreamDownloader.ViewModels;

/// <summary>
/// Tela de Filmes: categorias (com contagem), pesquisa instantânea e painel de detalhes.
/// </summary>
public partial class MoviesViewModel : ViewModelBase
{
    private readonly XtreamApiClient _api;
    private readonly MediaDownloadService _media;
    private readonly CatalogService _catalog;
    private readonly Database.DatabaseService _db;
    private readonly IRemoteFolderPicker _folderPicker;
    private readonly SettingsService _settings;
    private bool _forcePicker;
    private List<Movie> _allMovies = [];
    private CancellationTokenSource? _detailsCts;

    public ObservableCollection<XtreamCategory> Categories { get; } = [];
    public ObservableCollection<Movie> Movies { get; } = [];
    public ObservableCollection<Playlist> Playlists { get; } = [];

    [ObservableProperty] private Playlist? _selectedPlaylist;
    [ObservableProperty] private bool _showListPicker = true;

    [ObservableProperty]
    private XtreamCategory? _selectedCategory;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Movie? _selectedMovie;

    [ObservableProperty]
    private MovieDetails? _details;

    [ObservableProperty]
    private string _fileSizeText = "—";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Cadastre e escolha uma lista para carregar o conteúdo.";

    public MoviesViewModel(XtreamApiClient api, MediaDownloadService media,
        CatalogService catalog, Database.DatabaseService db, IRemoteFolderPicker folderPicker,
        SettingsService settings)
    {
        _api = api;
        _media = media;
        _catalog = catalog;
        _db = db;
        _folderPicker = folderPicker;
        _settings = settings;
    }

    /// <summary>Atualiza o seletor de listas; com uma só lista ativa, entra direto nela.</summary>
    public Task LoadAsync()
    {
        var keepId = SelectedPlaylist?.Id; // preserva a lista aberta ao renavegar
        Playlists.Clear();
        foreach (var p in _db.GetPlaylists(activeOnly: true))
            Playlists.Add(p);

        if (_forcePicker)
        {
            // usuário clicou em "Trocar lista": mostra o seletor sem auto-escolher
            _forcePicker = false;
            SelectedPlaylist = null;
        }
        else if (keepId is { } id && Playlists.FirstOrDefault(p => p.Id == id) is { } keep)
        {
            SelectedPlaylist = keep;
        }
        else if (Playlists.Count == 0)
        {
            SelectedPlaylist = null;
            ShowListPicker = true;
            StatusMessage = "Nenhuma lista ativa — cadastre sua lista M3U na aba Listas.";
        }
        else if (Playlists.FirstOrDefault(p => p.Id == _settings.LastPlaylistId) is { } last)
        {
            SelectedPlaylist = last; // lembra a última lista usada (entra direto ao abrir)
        }
        else if (Playlists.Count == 1)
        {
            SelectedPlaylist = Playlists[0]; // única lista: entra direto
        }
        else
        {
            SelectedPlaylist = null; // várias listas: usuário escolhe
        }
        return Task.CompletedTask;
    }

    partial void OnSelectedPlaylistChanged(Playlist? value)
    {
        ShowListPicker = value is null;
        if (value is not null)
        {
            _settings.LastPlaylistId = value.Id;
            _ = LoadCatalogAsync(value, forceRefresh: false);
        }
    }

    /// <summary>Pedido para abrir a aba Listas (botão "Adicionar lista" do seletor).</summary>
    public event Action? AddListRequested;

    [RelayCommand]
    private void AddList() => AddListRequested?.Invoke();

    /// <summary>Volta para a escolha de listas.</summary>
    [RelayCommand]
    private void ChangeList()
    {
        _forcePicker = true;
        SelectedPlaylist = null;
        _ = LoadAsync();
    }

    /// <summary>Sincronizar Agora: descarta o cache da lista atual e recarrega do servidor.</summary>
    [RelayCommand]
    private Task SyncAsync() =>
        SelectedPlaylist is null ? Task.CompletedTask : LoadCatalogAsync(SelectedPlaylist, forceRefresh: true);

    /// <summary>Recarrega tudo (nova conexão/lista trocada): invalida caches e volta ao seletor.</summary>
    public void Reset()
    {
        _allMovies = [];
        Movies.Clear();
        Categories.Clear();
        SelectedPlaylist = null;
        _ = LoadAsync();
    }

    private async Task LoadCatalogAsync(Playlist playlist, bool forceRefresh)
    {
        IsLoading = true;
        StatusMessage = $"Carregando filmes de \"{playlist.Name}\"...";
        try
        {
            var (categories, movies) = await _catalog.GetMoviesAsync(playlist, forceRefresh);
            _allMovies = movies;

            // contagem por categoria + item "Todos"
            var counts = _allMovies.GroupBy(m => m.CategoryId).ToDictionary(g => g.Key, g => g.Count());
            Categories.Clear();
            Categories.Add(new XtreamCategory { Id = "", Name = "Todos", Count = _allMovies.Count });
            foreach (var c in categories)
            {
                c.Count = counts.GetValueOrDefault(c.Id);
                Categories.Add(c);
            }
            SelectedCategory = Categories[0];
            ApplyFilter();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao carregar filmes: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedCategoryChanged(XtreamCategory? value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<Movie> result = _allMovies;
        if (!string.IsNullOrEmpty(SelectedCategory?.Id))
            result = result.Where(m => m.CategoryId == SelectedCategory.Id);
        if (!string.IsNullOrWhiteSpace(SearchText))
            result = result.Where(m => m.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));

        Movies.Clear();
        foreach (var m in result.OrderBy(m => m.Name))
            Movies.Add(m);
    }

    partial void OnSelectedMovieChanged(Movie? value)
    {
        _detailsCts?.Cancel();
        Details = null;
        FileSizeText = "—";
        if (value is null) return;

        _detailsCts = new CancellationTokenSource();
        _ = LoadDetailsAsync(value, _detailsCts.Token);
    }

    private async Task LoadDetailsAsync(Movie movie, CancellationToken ct)
    {
        try
        {
            var details = await _api.GetVodInfoAsync(movie.StreamId, ct);
            if (string.IsNullOrEmpty(details.Name)) details.Name = movie.Name;
            if (string.IsNullOrEmpty(details.CoverUrl)) details.CoverUrl = movie.Icon;
            if (ct.IsCancellationRequested) return;
            Details = details;

            var size = await _api.GetFileSizeAsync(_api.GetMovieUrl(movie), ct);
            if (!ct.IsCancellationRequested)
                FileSizeText = FormatHelper.Bytes(size);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!ct.IsCancellationRequested)
                Details = new MovieDetails { Name = movie.Name, Plot = "Não foi possível carregar os detalhes." };
        }
    }

    [RelayCommand]
    private async Task Download()
    {
        if (SelectedMovie is null) return;
        var remoteDir = await _folderPicker.PickAsync(); // popup: pasta de destino no FTP
        if (remoteDir is null)
        {
            StatusMessage = "Download cancelado.";
            return;
        }
        StatusMessage = await _media.EnqueueMovieAsync(SelectedMovie, Details?.Year, CategoryNameOf(SelectedMovie.CategoryId), remoteDir);
    }

    [RelayCommand]
    private async Task DownloadSelected(System.Collections.IList? selectedItems)
    {
        if (selectedItems == null || selectedItems.Count == 0) return;
        var remoteDir = await _folderPicker.PickAsync(); // uma pasta para todo o lote
        if (remoteDir is null)
        {
            StatusMessage = "Download cancelado.";
            return;
        }

        int count = selectedItems.Count;
        foreach (Movie m in selectedItems.Cast<Movie>())
        {
            await _media.EnqueueMovieAsync(m, null, CategoryNameOf(m.CategoryId), remoteDir);
        }
        StatusMessage = $"{count} filme(s) enviado(s) para a fila.";
    }

    /// <summary>Nome da categoria do filme (usado para deduzir [4K]/[L] no nome salvo).</summary>
    private string? CategoryNameOf(string categoryId) =>
        Categories.FirstOrDefault(c => c.Id == categoryId)?.Name;
}
