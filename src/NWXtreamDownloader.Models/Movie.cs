namespace NWXtreamDownloader.Models;

/// <summary>Filme (stream VOD) listado pelo servidor.</summary>
public class Movie
{
    public int StreamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    /// <summary>Extensão do arquivo (mp4, mkv...).</summary>
    public string Extension { get; set; } = "mp4";
    /// <summary>Ano de lançamento ("" se o servidor não informar).</summary>
    public string Year { get; set; } = string.Empty;
}
