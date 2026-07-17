using System.Text.Json;
using NWXtreamDownloader.Models;

namespace NWXtreamDownloader.Api;

/// <summary>
/// Cliente HTTP da API Xtream Codes (player_api.php).
/// Usa um único HttpClient reutilizado (injetado) e suporta cancelamento.
/// </summary>
public class XtreamApiClient
{
    private readonly HttpClient _http;

    public XtreamApiClient(HttpClient http) => _http = http;

    /// <summary>Credenciais da sessão atual (definidas após autenticar).</summary>
    public XtreamCredentials? Credentials { get; private set; }

    private string PlayerApi(string action = "") =>
        $"{Credentials!.BaseUrl}/player_api.php?username={Uri.EscapeDataString(Credentials.Username)}" +
        $"&password={Uri.EscapeDataString(Credentials.Password)}" +
        (action.Length > 0 ? $"&action={action}" : "");

    /// <summary>Autentica no servidor e retorna as informações da conta.</summary>
    public async Task<XtreamAccountInfo> AuthenticateAsync(XtreamCredentials credentials, CancellationToken ct = default)
    {
        Credentials = credentials;
        using var doc = await GetJsonAsync("", ct);
        if (!doc.RootElement.TryGetProperty("user_info", out var user))
            throw new InvalidOperationException("Resposta inválida do servidor.");

        var info = new XtreamAccountInfo
        {
            Authenticated = user.TryGetProperty("auth", out var auth) && auth.ToString() == "1",
            Status = user.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
            MaxConnections = user.TryGetProperty("max_connections", out var mc) && int.TryParse(mc.ToString(), out var m) ? m : 0,
        };
        if (user.TryGetProperty("exp_date", out var exp) && long.TryParse(exp.ToString(), out var unix))
            info.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(unix);

        if (!info.Authenticated)
            Credentials = null;
        return info;
    }

    /// <summary>Lista as categorias de filmes.</summary>
    public async Task<List<XtreamCategory>> GetVodCategoriesAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("get_vod_categories", ct);
        var list = new List<XtreamCategory>();
        foreach (var el in doc.RootElement.EnumerateArray())
            list.Add(new XtreamCategory
            {
                Id = Str(el, "category_id"),
                Name = Str(el, "category_name"),
            });
        return list;
    }

    /// <summary>Lista todos os filmes do servidor.</summary>
    public async Task<List<Movie>> GetVodStreamsAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("get_vod_streams", ct);
        var list = new List<Movie>();
        foreach (var el in doc.RootElement.EnumerateArray())
            list.Add(new Movie
            {
                StreamId = Int(el, "stream_id"),
                Name = Str(el, "name"),
                CategoryId = Str(el, "category_id"),
                Icon = Str(el, "stream_icon"),
                Extension = Str(el, "container_extension", "mp4"),
                Year = ParseYear(el),
            });
        return list;
    }

    /// <summary>Extrai o ano de "year"/"releaseDate"/"release_date" (formatos variam por servidor).</summary>
    private static string ParseYear(JsonElement el)
    {
        var raw = Str(el, "year", Str(el, "releaseDate", Str(el, "release_date", Str(el, "releasedate"))));
        return raw.Length >= 4 && int.TryParse(raw[..4], out var y) && y > 1800 ? raw[..4] : "";
    }

    /// <summary>Detalhes de um filme.</summary>
    public async Task<MovieDetails> GetVodInfoAsync(int streamId, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"get_vod_info&vod_id={streamId}", ct);
        var d = new MovieDetails();
        if (doc.RootElement.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            d.Name = Str(info, "name");
            d.Plot = Str(info, "plot", Str(info, "description"));
            d.Genre = Str(info, "genre");
            d.Duration = Str(info, "duration");
            d.CoverUrl = Str(info, "movie_image", Str(info, "cover_big"));
            var release = Str(info, "releasedate", Str(info, "release_date", Str(info, "year")));
            if (release.Length >= 4)
                d.Year = release[..4];
        }
        return d;
    }

    /// <summary>URL direta de download do filme.</summary>
    public string GetMovieUrl(Movie movie) =>
        $"{Credentials!.BaseUrl}/movie/{Credentials.Username}/{Credentials.Password}/{movie.StreamId}.{movie.Extension}";

    /// <summary>Tamanho do arquivo remoto via requisição HEAD (0 se indisponível).</summary>
    public async Task<long> GetFileSizeAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return response.Content.Headers.ContentLength ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Lista as categorias de séries.</summary>
    public async Task<List<XtreamCategory>> GetSeriesCategoriesAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("get_series_categories", ct);
        var list = new List<XtreamCategory>();
        foreach (var el in doc.RootElement.EnumerateArray())
            list.Add(new XtreamCategory { Id = Str(el, "category_id"), Name = Str(el, "category_name") });
        return list;
    }

    /// <summary>Lista todas as séries do servidor.</summary>
    public async Task<List<Series>> GetSeriesListAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("get_series", ct);
        var list = new List<Series>();
        foreach (var el in doc.RootElement.EnumerateArray())
            list.Add(new Series
            {
                SeriesId = Int(el, "series_id"),
                Name = Str(el, "name"),
                CategoryId = Str(el, "category_id"),
                Cover = Str(el, "cover"),
                Year = ParseYear(el),
            });
        return list;
    }

    /// <summary>Episódios de uma série (todas as temporadas).</summary>
    /// <summary>Episódios da série + ano extraído da ficha (info.releaseDate) quando disponível.</summary>
    public async Task<(string Year, List<Episode> Episodes)> GetSeriesEpisodesAsync(int seriesId, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"get_series_info&series_id={seriesId}", ct);
        var list = new List<Episode>();

        // ano vem da ficha da série (a listagem de muitos servidores não informa)
        var year = "";
        if (doc.RootElement.TryGetProperty("info", out var seriesInfo) && seriesInfo.ValueKind == JsonValueKind.Object)
            year = ParseYear(seriesInfo);

        if (!doc.RootElement.TryGetProperty("episodes", out var eps))
            return (year, list);

        // "episodes" pode ser objeto {"1":[...], "2":[...]} ou array de arrays
        var seasons = eps.ValueKind == JsonValueKind.Object
            ? eps.EnumerateObject().Select(p => p.Value)
            : eps.EnumerateArray().AsEnumerable();

        foreach (var season in seasons)
        {
            foreach (var el in season.EnumerateArray())
            {
                var title = "";
                if (el.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
                {
                    title = Str(info, "name");
                    if (string.IsNullOrWhiteSpace(title)) title = Str(info, "title");
                }
                if (string.IsNullOrWhiteSpace(title) || title.Contains($"S{Int(el, "season"):00}E{Int(el, "episode_num"):00}"))
                {
                    var rawTitle = Str(el, "title");
                    if (!string.IsNullOrWhiteSpace(rawTitle)) title = rawTitle;
                }
                
                list.Add(new Episode
                {
                    Id = Int(el, "id"),
                    Title = title,
                    Season = Int(el, "season"),
                    EpisodeNum = Int(el, "episode_num"),
                    Extension = Str(el, "container_extension", "mp4"),
                });
            }
        }
        return (year, list);
    }

    /// <summary>URL direta de download do episódio.</summary>
    public string GetEpisodeUrl(Episode ep) =>
        $"{Credentials!.BaseUrl}/series/{Credentials.Username}/{Credentials.Password}/{ep.Id}.{ep.Extension}";

    private static string Str(JsonElement el, string prop, string fallback = "") =>
        el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : fallback;

    private static int Int(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && int.TryParse(v.ToString(), out var i) ? i : 0;

    /// <summary>Executa uma ação da player_api e retorna o JSON bruto (usado pelas próximas etapas).</summary>
    public async Task<JsonDocument> GetJsonAsync(string action, CancellationToken ct = default)
    {
        if (Credentials is null && action.Length > 0)
            throw new InvalidOperationException("Não autenticado.");
        using var response = await _http.GetAsync(PlayerApi(action), ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}
