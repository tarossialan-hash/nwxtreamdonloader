using NWXtreamDownloader.Api;
using NWXtreamDownloader.Database;
using NWXtreamDownloader.Models;

namespace NWXtreamDownloader.Services;

/// <summary>
/// Catálogo por lista M3U com cache em memória: cada lista só é baixada do
/// servidor uma vez (ou quando o usuário pede "Sincronizar Agora").
/// Também alterna a conexão do <see cref="XtreamApiClient"/> para a lista ativa.
/// </summary>
public class CatalogService
{
    private readonly XtreamApiClient _api;
    private readonly DatabaseService _db;
    private readonly Dictionary<long, (List<XtreamCategory> Cats, List<Movie> Items)> _movies = [];
    private readonly Dictionary<long, (List<XtreamCategory> Cats, List<Series> Items)> _series = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _connectedId = -1;

    public CatalogService(XtreamApiClient api, DatabaseService db)
    {
        _api = api;
        _db = db;
    }

    public async Task<(List<XtreamCategory> Cats, List<Movie> Items)> GetMoviesAsync(Playlist p, bool forceRefresh = false)
    {
        await _lock.WaitAsync();
        try
        {
            if (!forceRefresh && _movies.TryGetValue(p.Id, out var cached))
            {
                await EnsureConnectedAsync(p, false);
                return cached;
            }
            await EnsureConnectedAsync(p, forceRefresh);
            var result = (await _api.GetVodCategoriesAsync(), await _api.GetVodStreamsAsync());
            _movies[p.Id] = result;
            _db.UpdatePlaylistSync(p.Id, result.Item2.Count, null);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(List<XtreamCategory> Cats, List<Series> Items)> GetSeriesAsync(Playlist p, bool forceRefresh = false)
    {
        await _lock.WaitAsync();
        try
        {
            if (!forceRefresh && _series.TryGetValue(p.Id, out var cached))
            {
                await EnsureConnectedAsync(p, false);
                return cached;
            }
            await EnsureConnectedAsync(p, forceRefresh);
            var result = (await _api.GetSeriesCategoriesAsync(), await _api.GetSeriesListAsync());
            _series[p.Id] = result;
            _db.UpdatePlaylistSync(p.Id, null, result.Item2.Count);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Conecta o cliente na lista informada e retorna os dados da conta
    /// (usado pelo Sincronizar da aba Listas e pela busca global antes de baixar).
    /// </summary>
    public async Task<XtreamAccountInfo> ConnectAsync(Playlist p)
    {
        await _lock.WaitAsync();
        try
        {
            var credentials = XtreamCredentials.FromM3uUrl(p.Url);
            var info = await _api.AuthenticateAsync(credentials);
            if (!info.Authenticated)
                throw new UnauthorizedAccessException($"Lista \"{p.Name}\": usuário ou senha inválidos.");
            _connectedId = p.Id;
            return info;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Autentica o cliente na lista informada (só quando muda de lista).</summary>
    private async Task EnsureConnectedAsync(Playlist p, bool force)
    {
        if (!force && _connectedId == p.Id) return;
        var credentials = XtreamCredentials.FromM3uUrl(p.Url);
        var info = await _api.AuthenticateAsync(credentials);
        if (!info.Authenticated)
            throw new UnauthorizedAccessException($"Lista \"{p.Name}\": usuário ou senha inválidos.");
        _connectedId = p.Id;
    }

    /// <summary>Invalida o cache (lista removida/alterada, ou nova conexão pela barra superior).</summary>
    public void Invalidate(long? playlistId = null)
    {
        if (playlistId is { } id)
        {
            _movies.Remove(id);
            _series.Remove(id);
        }
        else
        {
            _movies.Clear();
            _series.Clear();
        }
        _connectedId = -1;
    }
}
