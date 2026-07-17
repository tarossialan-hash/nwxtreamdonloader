namespace NWXtreamDownloader.Models;

/// <summary>Série listada pelo servidor.</summary>
public class Series
{
    public int SeriesId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string Cover { get; set; } = string.Empty;
    /// <summary>Ano de lançamento ("" se o servidor não informar).</summary>
    public string Year { get; set; } = string.Empty;
}
