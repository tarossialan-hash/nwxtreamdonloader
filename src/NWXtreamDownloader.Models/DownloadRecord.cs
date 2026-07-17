namespace NWXtreamDownloader.Models;

/// <summary>Registro de download persistido no SQLite.</summary>
public class DownloadRecord
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    /// <summary>Pasta remota escolhida para o envio SFTP ("" = padrão das configurações).</summary>
    public string RemoteDir { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
