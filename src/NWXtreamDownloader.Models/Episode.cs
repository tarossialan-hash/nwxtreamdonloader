namespace NWXtreamDownloader.Models;

/// <summary>Episódio de uma série.</summary>
public class Episode
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Season { get; set; }
    public int EpisodeNum { get; set; }
    public string Extension { get; set; } = "mp4";

    public string DisplayName => $"E{EpisodeNum:00}  {Title}".TrimEnd();
}
