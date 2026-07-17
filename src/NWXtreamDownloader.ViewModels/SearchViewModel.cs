using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.Database;
using NWXtreamDownloader.Services;

namespace NWXtreamDownloader.ViewModels;

/// <summary>Resultado da busca global (carrega o item original para permitir baixar).</summary>
public class SearchResult
{
    public string Name { get; init; } = "";
    public string ListName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Type { get; init; } = "";
    public Models.Playlist Playlist { get; init; } = null!;
    public Models.Movie? Movie { get; init; }
    public Models.Series? Series { get; init; }
}

/// <summary>Busca global: procura em todas as listas ativas, filmes e séries ao mesmo tempo.</summary>
public partial class SearchViewModel : ViewModelBase
{
    private const int MaxResults = 500;

    private readonly DatabaseService _db;
    private readonly CatalogService _catalog;
    private readonly MediaDownloadService _media;
    private readonly IRemoteFolderPicker _folderPicker;
    private readonly NWXtreamDownloader.Api.XtreamApiClient _api;

    public ObservableCollection<SearchResult> Results { get; } = [];

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusMessage = "Digite o que procura e pressione Buscar.";

    public SearchViewModel(DatabaseService db, CatalogService catalog,
        MediaDownloadService media, IRemoteFolderPicker folderPicker, NWXtreamDownloader.Api.XtreamApiClient api)
    {
        _db = db;
        _catalog = catalog;
        _media = media;
        _folderPicker = folderPicker;
        _api = api;
    }

    /// <summary>Baixar direto do resultado: popup da pasta FTP + fila (mesma lógica de Filmes/Séries).</summary>
    [RelayCommand]
    private async Task Download(SearchResult result)
    {
        var remoteDir = await _folderPicker.PickAsync();
        if (remoteDir is null)
        {
            StatusMessage = "Download cancelado.";
            return;
        }
        try
        {
            // garante que as URLs usam as credenciais da lista de origem do item
            await _catalog.ConnectAsync(result.Playlist);
            if (result.Movie is { } movie)
            {
                StatusMessage = await _media.EnqueueMovieAsync(movie, movie.Year, result.Category, remoteDir);
            }
            else if (result.Series is { } series)
            {
                StatusMessage = $"Carregando episódios de \"{series.Name}\"...";
                var (year, episodes) = await _api.GetSeriesEpisodesAsync(series.SeriesId);
                if (series.Year.Length == 0 && year.Length > 0)
                    series.Year = year;
                StatusMessage = _media.EnqueueEpisodes(series, result.Category, episodes, remoteDir);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✖ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var q = Query.Trim();
        Results.Clear();
        if (q.Length < 2)
        {
            StatusMessage = "Digite pelo menos 2 caracteres.";
            return;
        }

        IsSearching = true;
        var failed = 0;
        try
        {
            foreach (var playlist in _db.GetPlaylists(activeOnly: true))
            {
                StatusMessage = $"Buscando em \"{playlist.Name}\"...";
                try
                {
                    var (movieCats, movies) = await _catalog.GetMoviesAsync(playlist);
                    var movieCatNames = movieCats.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Name);
                    foreach (var m in movies.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    {
                        Results.Add(new SearchResult
                        {
                            Name = m.Name, ListName = playlist.Name,
                            Category = movieCatNames.GetValueOrDefault(m.CategoryId, "—"),
                            Type = "Filme", Playlist = playlist, Movie = m,
                        });
                        if (Results.Count >= MaxResults) break;
                    }

                    var (seriesCats, series) = await _catalog.GetSeriesAsync(playlist);
                    var seriesCatNames = seriesCats.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Name);
                    foreach (var s in series.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    {
                        Results.Add(new SearchResult
                        {
                            Name = s.Name, ListName = playlist.Name,
                            Category = seriesCatNames.GetValueOrDefault(s.CategoryId, "—"),
                            Type = "Série", Playlist = playlist, Series = s,
                        });
                        if (Results.Count >= MaxResults) break;
                    }
                }
                catch
                {
                    failed++;
                }
                if (Results.Count >= MaxResults) break;
            }

            StatusMessage = Results.Count >= MaxResults
                ? $"Mostrando os primeiros {MaxResults} resultados — refine a busca."
                : $"{Results.Count} resultado(s)" + (failed > 0 ? $" ({failed} lista(s) inacessível(is))" : "") + ".";
        }
        finally
        {
            IsSearching = false;
        }
    }
}
