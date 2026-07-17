namespace NWXtreamDownloader.Helpers;

/// <summary>Monta os caminhos de destino dos downloads (Filmes / Séries).</summary>
public static class PathBuilder
{
    public static string Sanitize(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "" : string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();

    /// <summary>Downloads\Filmes\Nome (Ano).ext</summary>
    public static string MoviePath(string root, string name, string? year, string ext) =>
        Path.Combine(root, "Filmes",
            $"{Sanitize(name)}{(string.IsNullOrEmpty(year) ? "" : $" ({year})")}.{ext}");

    public static string EpisodePath(string root, string series, string seasonFolder, int season, int episode, string episodeTitle, string ext)
    {
        var tag = $"S{season:00}E{episode:00}";
        var cleanTitle = Sanitize(episodeTitle);
        
        if (cleanTitle.Contains(tag, StringComparison.OrdinalIgnoreCase))
        {
            cleanTitle = cleanTitle.Replace(Sanitize(series), "", StringComparison.OrdinalIgnoreCase).Trim();
            cleanTitle = cleanTitle.Replace(tag, "", StringComparison.OrdinalIgnoreCase).Trim();
            cleanTitle = cleanTitle.Trim('-', ' ');
        }
        
        var filename = string.IsNullOrWhiteSpace(cleanTitle) 
            ? $"{Sanitize(series)} {tag}.{ext}"
            : $"{Sanitize(series)} {tag} - {cleanTitle}.{ext}";
            
        return Path.Combine(root, "Séries", Sanitize(series), Sanitize(seasonFolder), filename);
    }
}
