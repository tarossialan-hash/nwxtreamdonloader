using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.Api;
using NWXtreamDownloader.Models;
using NWXtreamDownloader.Services;

namespace NWXtreamDownloader.ViewModels;

/// <summary>Temporada com seus episódios (para exibição agrupada).</summary>
public class SeasonGroup
{
    public int Number { get; init; }
    public List<Episode> Episodes { get; init; } = [];
    public string Title => $"Temporada {Number:00}  ({Episodes.Count} ep.)";
}

/// <summary>
/// Tela de Séries: categorias, pesquisa, temporadas/episódios e downloads
/// (episódio, temporada inteira ou série completa).
/// </summary>
public partial class SeriesViewModel : ViewModelBase
{
    private readonly XtreamApiClient _api;
    private readonly MediaDownloadService _media;
    private readonly CatalogService _catalog;
    private readonly Database.DatabaseService _db;
    private readonly IRemoteFolderPicker _folderPicker;
    private readonly SettingsService _settings;
    private bool _forcePicker;
    private List<Series> _allSeries = [];
    private List<Episode> _episodes = [];
    private CancellationTokenSource? _episodesCts;

    public ObservableCollection<XtreamCategory> Categories { get; } = [];
    public ObservableCollection<Series> SeriesList { get; } = [];
    public ObservableCollection<SeasonGroup> Seasons { get; } = [];
    public ObservableCollection<Playlist> Playlists { get; } = [];

    [ObservableProperty] private Playlist? _selectedPlaylist;
    [ObservableProperty] private bool _showListPicker = true;

    [ObservableProperty]
    private XtreamCategory? _selectedCategory;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Series? _selectedSeries;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingEpisodes;

    [ObservableProperty]
    private string _statusMessage = "Cadastre e escolha uma lista para carregar o conteúdo.";

    public SeriesViewModel(XtreamApiClient api, MediaDownloadService media,
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
            SelectedPlaylist = last; // lembra a última lista usada
        }
        else if (Playlists.Count == 1)
        {
            SelectedPlaylist = Playlists[0];
        }
        else
        {
            SelectedPlaylist = null;
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

    /// <summary>Recarrega tudo (nova conexão/lista trocada).</summary>
    public void Reset()
    {
        _allSeries = [];
        SeriesList.Clear();
        Categories.Clear();
        Seasons.Clear();
        SelectedPlaylist = null;
        _ = LoadAsync();
    }

    private async Task LoadCatalogAsync(Playlist playlist, bool forceRefresh)
    {
        IsLoading = true;
        StatusMessage = $"Carregando séries de \"{playlist.Name}\"...";
        try
        {
            var (categories, series) = await _catalog.GetSeriesAsync(playlist, forceRefresh);
            _allSeries = series;

            var counts = _allSeries.GroupBy(s => s.CategoryId).ToDictionary(g => g.Key, g => g.Count());
            Categories.Clear();
            Categories.Add(new XtreamCategory { Id = "", Name = "Todas", Count = _allSeries.Count });
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
            StatusMessage = $"Erro ao carregar séries: {ex.Message}";
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
        IEnumerable<Series> result = _allSeries;
        if (!string.IsNullOrEmpty(SelectedCategory?.Id))
            result = result.Where(s => s.CategoryId == SelectedCategory.Id);
        if (!string.IsNullOrWhiteSpace(SearchText))
            result = result.Where(s => s.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));

        SeriesList.Clear();
        foreach (var s in result.OrderBy(s => s.Name))
            SeriesList.Add(s);
    }

    partial void OnSelectedSeriesChanged(Series? value)
    {
        _episodesCts?.Cancel();
        Seasons.Clear();
        _episodes = [];
        if (value is null) return;

        _episodesCts = new CancellationTokenSource();
        _ = LoadEpisodesAsync(value, _episodesCts.Token);
    }

    private async Task LoadEpisodesAsync(Series series, CancellationToken ct)
    {
        IsLoadingEpisodes = true;
        try
        {
            var (year, episodes) = await _api.GetSeriesEpisodesAsync(series.SeriesId, ct);
            if (ct.IsCancellationRequested) return;
            if (series.Year.Length == 0 && year.Length > 0)
                series.Year = year; // ano da ficha para salvar junto com o nome
            _episodes = episodes;
            foreach (var group in episodes.GroupBy(e => e.Season).OrderBy(g => g.Key))
                Seasons.Add(new SeasonGroup
                {
                    Number = group.Key,
                    Episodes = group.OrderBy(e => e.EpisodeNum).ToList(),
                });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                StatusMessage = $"Erro ao carregar episódios: {ex.Message}";
        }
        finally
        {
            IsLoadingEpisodes = false;
        }
    }

    /// <summary>Popup de pasta remota; null = usuário cancelou.</summary>
    private async Task<string?> PickRemoteDirAsync()
    {
        var dir = await _folderPicker.PickAsync();
        if (dir is null)
            StatusMessage = "Download cancelado.";
        return dir;
    }

    [RelayCommand]
    private async Task DownloadEpisode(Episode episode)
    {
        if (SelectedSeries is null) return;
        if (await PickRemoteDirAsync() is not { } remoteDir) return;
        StatusMessage = _media.EnqueueEpisodes(SelectedSeries, CategoryNameOf(SelectedSeries.CategoryId), [episode], remoteDir);
    }

    [RelayCommand]
    private async Task DownloadSeason(SeasonGroup season)
    {
        if (SelectedSeries is null) return;
        if (await PickRemoteDirAsync() is not { } remoteDir) return;
        StatusMessage = _media.EnqueueEpisodes(SelectedSeries, CategoryNameOf(SelectedSeries.CategoryId), season.Episodes, remoteDir);
    }

    [RelayCommand]
    private async Task DownloadSeries()
    {
        if (SelectedSeries is null || _episodes.Count == 0) return;
        if (await PickRemoteDirAsync() is not { } remoteDir) return;
        StatusMessage = _media.EnqueueEpisodes(SelectedSeries, CategoryNameOf(SelectedSeries.CategoryId), _episodes, remoteDir);
    }

    private string _batchRemoteDir = "";

    [RelayCommand]
    private async Task DownloadSelected(System.Collections.IList? selectedItems)
    {
        if (selectedItems == null || selectedItems.Count == 0) return;
        if (await PickRemoteDirAsync() is not { } remoteDir) return;
        _batchRemoteDir = remoteDir;

        IsLoading = true;
        StatusMessage = $"Enfileirando {selectedItems.Count} séries...";

        foreach (Series s in selectedItems.Cast<Series>().ToList())
        {
            try
            {
                var (year, episodes) = await _api.GetSeriesEpisodesAsync(s.SeriesId, CancellationToken.None);
                if (s.Year.Length == 0 && year.Length > 0)
                    s.Year = year;
                if (episodes.Any())
                {
                    _media.EnqueueEpisodes(s, CategoryNameOf(s.CategoryId), episodes, _batchRemoteDir);
                }
            }
            catch { }
        }

        StatusMessage = $"{selectedItems.Count} série(s) enfileirada(s).";
        IsLoading = false;
    }

    /// <summary>Nome da categoria da série (usado para deduzir [4K]/[L] no nome salvo).</summary>
    private string? CategoryNameOf(string categoryId) =>
        Categories.FirstOrDefault(c => c.Id == categoryId)?.Name;
}
