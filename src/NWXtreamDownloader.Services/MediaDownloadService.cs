using NWXtreamDownloader.Api;
using NWXtreamDownloader.DownloadManager;
using NWXtreamDownloader.Helpers;
using NWXtreamDownloader.Models;

namespace NWXtreamDownloader.Services;

/// <summary>
/// Monta o caminho de destino (Downloads\Filmes / Downloads\Séries\...\Temporada NN)
/// e enfileira filmes e episódios no gerenciador de downloads.
/// </summary>
public class MediaDownloadService
{
    private readonly XtreamApiClient _api;
    private readonly DownloadService _downloads;
    private readonly SettingsService _settings;

    public MediaDownloadService(XtreamApiClient api, DownloadService downloads, SettingsService settings)
    {
        _api = api;
        _downloads = downloads;
        _settings = settings;
    }

    /// <summary>
    /// Nome de identificação para salvar: "Nome (Ano) [4K] [L]".
    /// Ano vem do servidor; [4K]/[L] são deduzidos do nome da categoria
    /// (ex.: "4K Ultra", "Legendados") quando o nome ainda não os contém.
    /// </summary>
    private static string Decorate(string name, string? year, string? categoryName)
    {
        var n = name.Trim();
        if (!string.IsNullOrWhiteSpace(year) && !n.Contains($"({year})"))
            n += $" ({year})";
        var cat = categoryName ?? "";
        if (cat.Contains("4k", StringComparison.OrdinalIgnoreCase) &&
            !n.Contains("4k", StringComparison.OrdinalIgnoreCase))
            n += " [4K]";
        if (cat.Contains("legendado", StringComparison.OrdinalIgnoreCase) &&
            !n.Contains("[L]", StringComparison.OrdinalIgnoreCase) &&
            !n.Contains("legendado", StringComparison.OrdinalIgnoreCase))
            n += " [L]";
        return n;
    }

    public async Task<string> EnqueueMovieAsync(Movie movie, string? year, string? categoryName, string remoteDir = "")
    {
        var y = string.IsNullOrWhiteSpace(year) ? movie.Year : year;
        if (string.IsNullOrWhiteSpace(y) && !movie.Name.Contains('('))
        {
            // listagem sem ano: busca na ficha do filme para salvar "Nome (Ano)"
            try { y = (await _api.GetVodInfoAsync(movie.StreamId)).Year; } catch { }
        }
        var name = Decorate(movie.Name, y, categoryName);
        var dest = PathBuilder.MoviePath(_settings.DownloadFolder, name, null, movie.Extension);
        return _downloads.Enqueue(name, _api.GetMovieUrl(movie), dest, _settings.OverwriteExisting, "Filmes", remoteDir)
            ? $"\"{name}\" adicionado à fila."
            : $"\"{name}\" já foi baixado — ignorado.";
    }

    /// <summary>Enfileira vários episódios; retorna resumo (adicionados / ignorados).</summary>
    public string EnqueueEpisodes(Series series, string? categoryName, IEnumerable<Episode> episodes, string remoteDir = "")
    {
        episodes = episodes.ToList(); // enumerado duas vezes (fila + resumo)
        int added = 0, skipped = 0;
        var pattern = _settings.SeasonFolderPattern;
        var seriesLabel = Decorate(series.Name, series.Year, categoryName);
        foreach (var ep in episodes)
        {
            var seasonFolder = pattern
                .Replace("{nn}", ep.Season.ToString("00"))
                .Replace("{n}", ep.Season.ToString());
            var dest = PathBuilder.EpisodePath(_settings.DownloadFolder, seriesLabel, seasonFolder, ep.Season, ep.EpisodeNum, ep.Title, ep.Extension);
            var title = $"{seriesLabel} S{ep.Season:00}E{ep.EpisodeNum:00}";
            if (!string.IsNullOrWhiteSpace(ep.Title) && !ep.Title.Contains($"S{ep.Season:00}E{ep.EpisodeNum:00}", StringComparison.OrdinalIgnoreCase))
                title += $" - {ep.Title}";
            if (_downloads.Enqueue(title, _api.GetEpisodeUrl(ep), dest, _settings.OverwriteExisting, seriesLabel, remoteDir))
                added++;
            else
                skipped++;
        }
        return skipped == 0
            ? $"{added} episódio(s) adicionado(s) à fila."
            : $"{added} adicionado(s), {skipped} já baixado(s) — ignorado(s).";
    }
}
