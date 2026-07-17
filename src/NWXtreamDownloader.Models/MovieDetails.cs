namespace NWXtreamDownloader.Models;

/// <summary>Detalhes de um filme (get_vod_info).</summary>
public class MovieDetails
{
    public string Name { get; set; } = string.Empty;
    public string Plot { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    /// <summary>Tamanho do arquivo em bytes (0 = desconhecido).</summary>
    public long SizeBytes { get; set; }
}
