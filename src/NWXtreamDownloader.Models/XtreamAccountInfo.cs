namespace NWXtreamDownloader.Models;

/// <summary>Informações da conta retornadas pelo servidor após autenticação.</summary>
public class XtreamAccountInfo
{
    public bool Authenticated { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public int MaxConnections { get; set; }
}
