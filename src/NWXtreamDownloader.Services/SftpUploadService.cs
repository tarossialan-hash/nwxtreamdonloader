using Renci.SshNet;
using NWXtreamDownloader.DownloadManager;
using NWXtreamDownloader.Helpers;

namespace NWXtreamDownloader.Services;

/// <summary>
/// Envia arquivos baixados para o servidor via SFTP (SSH), replicando a
/// estrutura local (Filmes/, Séries/Nome/temp N/) dentro da pasta remota base.
/// </summary>
public class SftpUploadService
{
    private readonly SettingsService _settings;

    // navegação/teste/criação usam 1 conexão persistente por vez
    private readonly SemaphoreSlim _gate = new(1, 1);

    // uploads paralelos (configurável), com pool de conexões reutilizadas
    private SemaphoreSlim _uploadGate = new(2, 2);
    private int _maxUploads = 2;

    /// <summary>Envios simultâneos (1 a 4). Vale para os próximos envios da fila.</summary>
    public int MaxUploads
    {
        get => _maxUploads;
        set
        {
            var v = Math.Clamp(value, 1, 4);
            if (v == _maxUploads) return;
            _maxUploads = v;
            _uploadGate = new SemaphoreSlim(v, v); // envios em andamento seguram o gate antigo
        }
    }
    private readonly System.Collections.Concurrent.ConcurrentBag<SftpClient> _pool = [];
    private string _poolKey = "";

    // conexão persistente (reutilizada entre navegações) + cache de pastas
    private SftpClient? _client;
    private string _clientKey = "";
    private readonly Dictionary<string, List<string>> _dirCache = [];

    /// <summary>Marshaling para a thread de UI (definido pelo App).</summary>
    public Action<Action> UiInvoke { get; set; } = a => a();

    private readonly NewContentLog _newContent;

    public SftpUploadService(SettingsService settings, NewContentLog newContent)
    {
        _settings = settings;
        _newContent = newContent;
    }

    /// <summary>Envia o arquivo de um download concluído (chamado pelo evento Completed).</summary>
    public async Task UploadAsync(DownloadItem item, bool force = false)
    {
        if (!force && !_settings.SftpEnabled)
        {
            // nunca falhar em silêncio: mostra o motivo no item
            SetStatus(item, "Envio SFTP desativado — ative na aba Servidor FTP");
            return;
        }
        SetStatus(item, "Na fila de envio...");
        var gate = _uploadGate; // captura: o gate pode ser trocado nas configurações
        await gate.WaitAsync();
        try
        {
            await Task.Run(() => Upload(item));
        }
        finally
        {
            gate.Release();
        }
    }

    private void Upload(DownloadItem item)
    {
        if (!File.Exists(item.DestinationPath))
        {
            SetStatus(item, "✖ Arquivo local não existe mais (já foi enviado e apagado)");
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            SetStatus(item, attempt == 1 ? "Enviando ao servidor..." : $"Enviando (tentativa {attempt}/3)...");
            SftpClient? client = null;
            try
            {
                client = RentUploadClient();

                // caminho remoto = pasta escolhida no popup (ou a padrão) + caminho
                // relativo SEM a raiz local ("Filmes"/"Séries"):
                // ex. /home/Novelas/NomeDaSérie/temp 1/S01E01.mkv
                var baseDir = string.IsNullOrWhiteSpace(item.RemoteDir) ? _settings.SftpRemoteDir : item.RemoteDir;
                var relative = Path.GetRelativePath(_settings.DownloadFolder, item.DestinationPath)
                    .Replace('\\', '/');
                var slash = relative.IndexOf('/');
                if (slash > 0)
                    relative = relative[(slash + 1)..];
                var remotePath = baseDir.TrimEnd('/') + "/" + relative;

                EnsureRemoteDirectories(client, remotePath[..remotePath.LastIndexOf('/')]);

                // progresso do envio: atualiza a cada ~2%
                var totalBytes = new FileInfo(item.DestinationPath).Length;
                long lastReport = 0;
                var step = Math.Max(totalBytes / 50, 1);
                using (var file = File.OpenRead(item.DestinationPath))
                {
                    client.UploadFile(file, remotePath, canOverride: true, uploaded =>
                    {
                        var sent = (long)uploaded;
                        if (sent - lastReport < step) return;
                        lastReport = sent;
                        var pct = totalBytes > 0 ? (int)(sent * 100 / totalBytes) : 0;
                        SetStatus(item, $"Enviando ao servidor... {pct}% ({FormatHelper.Bytes(sent)} de {FormatHelper.Bytes(totalBytes)})");
                    });
                }
                ReturnUploadClient(client);
                try
                {
                    File.Delete(item.DestinationPath);
                    DeleteEmptyParents(Path.GetDirectoryName(item.DestinationPath), _settings.DownloadFolder);
                }
                catch { }
                SetStatus(item, "✔ Enviado e apagado do PC");

                // Novos Conteúdos: só entra na lista após o envio 100% concluído,
                // identificado pelo nome da pasta do FTP: [Novelas] Nome (Ano) - temp 1 (50 ep.)
                var trimmed = baseDir.TrimEnd('/');
                var ftpFolder = trimmed.Length == 0 ? "/" : trimmed[(trimmed.LastIndexOf('/') + 1)..];
                if (item.Category == "Filmes")
                {
                    _newContent.AddMovie(ftpFolder, item.Title);
                }
                else if (item.Category.Length > 0)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(item.Title, @"S(\d{1,3})E\d");
                    var season = match.Success ? int.Parse(match.Groups[1].Value) : 0;
                    _newContent.AddUploadedEpisode(ftpFolder, item.Category, season);
                }
                return;
            }
            catch (Exception ex)
            {
                Logger.Error($"SFTP upload (tentativa {attempt})", ex);
                try { client?.Dispose(); } catch { } // conexão suspeita: não volta ao pool
                if (attempt >= 3)
                {
                    SetStatus(item, "✖ Falha no envio — use o botão de reenvio (detalhes no app.log)");
                    return;
                }
                Thread.Sleep(TimeSpan.FromSeconds(15 * attempt)); // dá fôlego ao servidor
            }
        }
    }

    /// <summary>Pega uma conexão do pool de uploads (ou abre uma nova).</summary>
    private SftpClient RentUploadClient()
    {
        var key = $"{_settings.SftpHost}:{_settings.SftpPort}:{_settings.SftpUser}:{_settings.SftpPassword}";
        lock (_pool)
        {
            if (_poolKey != key)
            {
                while (_pool.TryTake(out var stale))
                    try { stale.Dispose(); } catch { }
                _poolKey = key;
            }
        }
        while (_pool.TryTake(out var c))
        {
            if (c.IsConnected) return c;
            try { c.Dispose(); } catch { }
        }
        var client = new SftpClient(_settings.SftpHost, _settings.SftpPort, _settings.SftpUser, _settings.SftpPassword);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(20);
        client.OperationTimeout = TimeSpan.FromMinutes(5);
        client.Connect();
        return client;
    }

    private void ReturnUploadClient(SftpClient client)
    {
        if (client.IsConnected)
            _pool.Add(client);
        else
            try { client.Dispose(); } catch { }
    }

    /// <summary>
    /// Verificação rápida da conexão (indicador verde/vermelho): reutiliza a
    /// conexão persistente; se caiu, tenta reconectar uma vez.
    /// </summary>
    public async Task<bool> PingAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.SftpHost) || string.IsNullOrWhiteSpace(_settings.SftpUser))
            return false;
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                try { return GetClient().IsConnected; }
                catch { return false; }
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Testa a conexão e o acesso à pasta remota. Retorna mensagem amigável.</summary>
    public async Task<string> TestAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await TestCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> TestCoreAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var client = GetClient();
                if (!client.Exists(_settings.SftpRemoteDir))
                    return $"✖ Conectou, mas a pasta remota \"{_settings.SftpRemoteDir}\" não existe.";
                client.ListDirectory(_settings.SftpRemoteDir);
                return "✔ SFTP OK — conexão e pasta remota acessíveis.";
            }
            catch (Exception ex)
            {
                Logger.Error("SFTP teste", ex);
                return $"✖ Falha no SFTP: {ex.Message}";
            }
        });
    }

    /// <summary>
    /// Lista as subpastas de um diretório remoto. Pastas já visitadas vêm do cache
    /// (instantâneo, sem tocar no servidor); a conexão é reutilizada entre chamadas.
    /// </summary>
    public async Task<List<string>> ListDirectoriesAsync(string path, bool forceRefresh = false)
    {
        var key = string.IsNullOrWhiteSpace(path) ? "/" : path;
        if (!forceRefresh)
        {
            lock (_dirCache)
                if (_dirCache.TryGetValue(key, out var cached))
                    return cached;
        }

        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var client = GetClient();
                var dirs = client.ListDirectory(key)
                    .Where(f => f.IsDirectory && f.Name is not ("." or ".."))
                    .Select(f => f.FullName)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                lock (_dirCache)
                    _dirCache[key] = dirs;
                return dirs;
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Limpa o cache de pastas: a próxima navegação recarrega tudo do servidor.</summary>
    public void ClearDirCache()
    {
        lock (_dirCache) _dirCache.Clear();
    }

    /// <summary>Cria uma pasta remota.</summary>
    public async Task CreateDirectoryAsync(string path)
    {
        await _gate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                var client = GetClient();
                client.CreateDirectory(path);
            });
            InvalidateDirCache(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Remove do cache o caminho e o pai (estrutura mudou).</summary>
    private void InvalidateDirCache(string path)
    {
        var p = path.TrimEnd('/');
        var parent = p.LastIndexOf('/') <= 0 ? "/" : p[..p.LastIndexOf('/')];
        lock (_dirCache)
        {
            _dirCache.Remove(p);
            _dirCache.Remove(parent);
        }
    }

    /// <summary>Exclui uma pasta remota com TODO o conteúdo (recursivo). A UI exige confirmação antes.</summary>
    public async Task DeleteDirectoryAsync(string path)
    {
        await _gate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                var client = GetClient();
                DeleteRecursive(client, path);
            });
            InvalidateDirCache(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void DeleteRecursive(SftpClient client, string path)
    {
        foreach (var entry in client.ListDirectory(path))
        {
            if (entry.Name is "." or "..") continue;
            if (entry.IsDirectory)
                DeleteRecursive(client, entry.FullName);
            else
                client.DeleteFile(entry.FullName);
        }
        client.DeleteDirectory(path);
    }

    /// <summary>
    /// Conexão persistente: abre uma vez e reutiliza em todas as operações.
    /// Reconecta sozinho se cair ou se as credenciais mudarem nas configurações.
    /// </summary>
    private SftpClient GetClient()
    {
        var key = $"{_settings.SftpHost}:{_settings.SftpPort}:{_settings.SftpUser}:{_settings.SftpPassword}";
        if (_client is { IsConnected: true } && _clientKey == key)
            return _client;

        DisposeClient();
        var client = new SftpClient(_settings.SftpHost, _settings.SftpPort, _settings.SftpUser, _settings.SftpPassword);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(20);   // conexão não fica pendurada
        client.OperationTimeout = TimeSpan.FromMinutes(5);          // operações de arquivos grandes
        client.Connect();
        _client = client;
        _clientKey = key;
        lock (_dirCache) _dirCache.Clear(); // servidor pode ter mudado
        return client;
    }

    private void DisposeClient()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
    }

    /// <summary>Cria a árvore de diretórios remota, nível a nível.</summary>
    private static void EnsureRemoteDirectories(SftpClient client, string dir)
    {
        var current = "";
        foreach (var part in dir.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + part;
            if (!client.Exists(current))
            {
                // outro upload paralelo pode ter criado a pasta no meio tempo
                try { client.CreateDirectory(current); }
                catch { if (!client.Exists(current)) throw; }
            }
        }
    }

    /// <summary>
    /// Após apagar o arquivo, remove as pastas que ficaram vazias subindo na árvore
    /// (temporada → série → Séries), parando na pasta raiz de downloads.
    /// </summary>
    private static void DeleteEmptyParents(string? dir, string root)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd('\\', '/');
        while (!string.IsNullOrEmpty(dir))
        {
            var full = Path.GetFullPath(dir).TrimEnd('\\', '/');
            // nunca apaga a raiz nem nada fora dela
            if (full.Length <= rootFull.Length ||
                !full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                break;
            if (Directory.EnumerateFileSystemEntries(full).Any())
                break; // pasta ainda tem conteúdo (outros episódios baixando etc.)
            Directory.Delete(full);
            dir = Path.GetDirectoryName(full);
        }
    }

    private void SetStatus(DownloadItem item, string text) =>
        UiInvoke(() => item.UploadText = text);
}
