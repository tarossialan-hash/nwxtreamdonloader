namespace NWXtreamDownloader.Models;

/// <summary>Lista M3U cadastrada (aba Listas).</summary>
public class Playlist
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public DateTime? LastSync { get; set; }
    public int MovieCount { get; set; }
    public int SeriesCount { get; set; }
    /// <summary>Data de vencimento da conta ("" se desconhecida).</summary>
    public string ExpiresAt { get; set; } = string.Empty;

    /// <summary>Resumo curto da URL para exibição: só DNS, usuário e senha.</summary>
    public string UrlSummary
    {
        get
        {
            try
            {
                var c = XtreamCredentials.FromM3uUrl(Url);
                var host = Uri.TryCreate(c.Server, UriKind.Absolute, out var u) ? u.Host : c.Server;
                return $"{host}   usuário: {c.Username}   senha: {c.Password}";
            }
            catch
            {
                return Url;
            }
        }
    }

    public string SyncInfo => (LastSync is null
        ? "Nunca sincronizada"
        : $"Sincronizada em {LastSync:dd/MM HH:mm} — {MovieCount} filmes, {SeriesCount} séries")
        + (ExpiresAt.Length > 0 ? $" — vence em {ExpiresAt}" : "");
}
