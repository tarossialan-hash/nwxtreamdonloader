using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using NWXtreamDownloader.Database;
using NWXtreamDownloader.Helpers;

namespace NWXtreamDownloader.DownloadManager;

/// <summary>
/// Gerenciador de downloads: fila com concorrência configurável, pausa/retomada
/// (HTTP Range), cancelamento, velocidade, ETA, limite de velocidade e
/// persistência de estado no SQLite. Continua baixando com a janela minimizada.
/// </summary>
public class DownloadService
{
    private readonly HttpClient _http;
    private readonly DatabaseService _db;
    private readonly List<DownloadItem> _active = [];

    public ObservableCollection<DownloadItem> Items { get; } = [];

    /// <summary>Marshaling para a thread de UI (definido pelo App na inicialização).</summary>
    public Action<Action> UiInvoke { get; set; } = a => a();

    public int MaxConcurrent { get; set; } = 2;
    /// <summary>Conexões paralelas por arquivo (download segmentado). 1 = desligado.</summary>
    public int SegmentsPerDownload { get; set; } = 4;
    /// <summary>Limite de velocidade em KB/s por download (0 = ilimitado).</summary>
    public int SpeedLimitKB { get; set; }

    /// <summary>Disparado (em thread de fundo) quando um download é concluído.</summary>
    public event Action<DownloadItem>? Completed;

    public DownloadService(HttpClient http, DatabaseService db)
    {
        _http = http;
        _db = db;
    }

    /// <summary>Restaura downloads pendentes do banco (como pausados).</summary>
    public void LoadPending()
    {
        foreach (var r in _db.GetDownloads(pendingOnly: true))
        {
            var item = new DownloadItem
            {
                DbId = r.Id, Title = r.Title, Url = r.Url, DestinationPath = r.Path,
                Category = r.Category, RemoteDir = r.RemoteDir,
                TotalBytes = r.TotalBytes, Status = DownloadStatus.Paused,
            };
            // downloads segmentados: o .part é pré-alocado, o progresso real está no .seg
            if (File.Exists(item.TempPath + ".seg"))
            {
                long sum = 0;
                foreach (var part in File.ReadAllText(item.TempPath + ".seg").Split(';'))
                    if (long.TryParse(part, out var v)) sum += v;
                item.ReceivedBytes = sum;
            }
            else if (File.Exists(item.TempPath))
            {
                item.ReceivedBytes = new FileInfo(item.TempPath).Length;
            }
            if (item.TotalBytes > 0)
                item.Progress = item.ReceivedBytes * 100.0 / item.TotalBytes;
            item.SizeText = $"{FormatHelper.Bytes(item.ReceivedBytes)} / {FormatHelper.Bytes(item.TotalBytes)}";
            Items.Add(item);
        }
    }

    /// <summary>Adiciona à fila. Retorna false se o arquivo já existe (e substituir está desativado).</summary>
    public bool Enqueue(string title, string url, string destPath, bool overwrite, string category = "", string remoteDir = "")
    {
        if (!overwrite && (File.Exists(destPath) ||
            Items.Any(i => i.DestinationPath == destPath && i.Status is not (DownloadStatus.Canceled or DownloadStatus.Failed))))
            return false;

        var item = new DownloadItem { Title = title, Url = url, DestinationPath = destPath, Category = category, RemoteDir = remoteDir };
        item.DbId = _db.InsertDownload(title, url, destPath, category, remoteDir);
        UiInvoke(() => { Items.Add(item); Pump(); });
        return true;
    }

    /// <summary>Inicia/retoma todos os itens parados, com falha ou cancelados.</summary>
    public void StartAll() => UiInvoke(() =>
    {
        foreach (var i in Items.Where(i => i.Status is DownloadStatus.Paused or DownloadStatus.Failed or DownloadStatus.Canceled).ToList())
        {
            i.Status = DownloadStatus.Queued;
            Persist(i);
        }
        Pump();
    });

    /// <summary>Pausa todos os downloads ativos e em fila.</summary>
    public void PauseAll() => UiInvoke(() =>
    {
        foreach (var i in Items.ToList()) Pause(i);
    });

    /// <summary>Cancela todos os downloads não concluídos.</summary>
    public void CancelAll() => UiInvoke(() =>
    {
        foreach (var i in Items.ToList()) Cancel(i);
    });

    /// <summary>Remove todos os itens da lista (cancela os ativos).</summary>
    public void ClearAll() => UiInvoke(() =>
    {
        foreach (var i in Items.ToList()) Remove(i);
    });

    public void Pause(DownloadItem item)
    {
        if (item.Status == DownloadStatus.Downloading)
        {
            item.PauseRequested = true;
            item.Cts?.Cancel();
        }
        else if (item.Status == DownloadStatus.Queued)
        {
            item.Status = DownloadStatus.Paused;
            Persist(item);
        }
    }

    public void Resume(DownloadItem item)
    {
        if (item.Status is DownloadStatus.Paused or DownloadStatus.Failed or DownloadStatus.Canceled)
        {
            item.Status = DownloadStatus.Queued;
            Persist(item);
            UiInvoke(Pump);
        }
    }

    public void Cancel(DownloadItem item)
    {
        if (item.Status == DownloadStatus.Downloading)
        {
            item.Cts?.Cancel();
        }
        else if (item.Status is DownloadStatus.Queued or DownloadStatus.Paused)
        {
            item.Status = DownloadStatus.Canceled;
            TryDelete(item.TempPath);
            TryDelete(item.TempPath + ".seg");
            Persist(item);
        }
    }

    public void Remove(DownloadItem item)
    {
        Cancel(item);
        if (item.Status != DownloadStatus.Completed)
            _db.DeleteDownload(item.DbId);
        UiInvoke(() => Items.Remove(item));
    }

    /// <summary>Inicia os próximos itens da fila enquanto houver vagas. Executar na thread de UI.</summary>
    private void Pump()
    {
        while (_active.Count < MaxConcurrent)
        {
            var next = Items.FirstOrDefault(i => i.Status == DownloadStatus.Queued);
            if (next is null) return;
            _active.Add(next);
            next.Cts = new CancellationTokenSource();
            next.Status = DownloadStatus.Downloading;
            var ct = next.Cts.Token;
            _ = Task.Run(() => RunAsync(next, ct), CancellationToken.None);
        }
    }

    private async Task RunAsync(DownloadItem item, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.DestinationPath)!);

            // download SEGMENTADO (várias conexões na mesma URL) quando o servidor
            // suporta Range — contorna o limite de velocidade por conexão.
            var segSidecar = item.TempPath + ".seg";
            var legacyPartial = File.Exists(item.TempPath) && !File.Exists(segSidecar);
            long total = 0;
            var segmented = false;
            if (!legacyPartial && SegmentsPerDownload > 1)
            {
                var (probeTotal, supportsRanges) = await ProbeAsync(item.Url, ct);
                if (supportsRanges && probeTotal > 20_000_000)
                {
                    total = probeTotal;
                    segmented = true;
                    await DownloadSegmentedAsync(item, total, ct);
                }
            }
            if (!segmented)
                total = await DownloadSingleAsync(item, ct);

            File.Move(item.TempPath, item.DestinationPath, overwrite: true);
            TryDelete(item.TempPath + ".seg");
            UiInvoke(() =>
            {
                item.ReceivedBytes = total;
                item.Progress = 100;
                item.SpeedText = "";
                item.EtaText = "";
                item.SizeText = FormatHelper.Bytes(total);
                item.Status = DownloadStatus.Completed;
            });
            _db.UpdateDownload(item.DbId, nameof(DownloadStatus.Completed), total, completed: true);
            Completed?.Invoke(item);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // timeout de rede (não foi o usuário): marca como falha, o .part fica para retomar
            UiInvoke(() =>
            {
                item.SpeedText = "";
                item.EtaText = "";
                item.Status = DownloadStatus.Failed;
            });
            _db.UpdateDownload(item.DbId, nameof(DownloadStatus.Failed), item.TotalBytes);
        }
        catch (OperationCanceledException)
        {
            var paused = item.PauseRequested;
            item.PauseRequested = false;
            if (!paused)
            {
                TryDelete(item.TempPath);
                TryDelete(item.TempPath + ".seg");
            }
            var status = paused ? DownloadStatus.Paused : DownloadStatus.Canceled;
            UiInvoke(() =>
            {
                item.SpeedText = "";
                item.EtaText = "";
                item.Status = status;
            });
            _db.UpdateDownload(item.DbId, status.ToString(), item.TotalBytes);
        }
        catch (Exception)
        {
            UiInvoke(() =>
            {
                item.SpeedText = "";
                item.EtaText = "";
                item.Status = DownloadStatus.Failed;
            });
            _db.UpdateDownload(item.DbId, nameof(DownloadStatus.Failed), item.TotalBytes);
        }
        finally
        {
            UiInvoke(() => { _active.Remove(item); Pump(); });
        }
    }

    /// <summary>Download tradicional em 1 conexão, com retomada via Range. Retorna o tamanho total.</summary>
    private async Task<long> DownloadSingleAsync(DownloadItem item, CancellationToken ct)
    {
            long existing = File.Exists(item.TempPath) ? new FileInfo(item.TempPath).Length : 0;
            using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
            if (existing > 0)
                request.Headers.Range = new RangeHeaderValue(existing, null);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                existing = 0; // servidor não suporta retomada: recomeça
            response.EnsureSuccessStatusCode();

            var total = existing + (response.Content.Headers.ContentLength ?? 0);
            UiInvoke(() => item.TotalBytes = total);

            var buffer = new byte[81920];
            long received = existing;
            var sw = Stopwatch.StartNew();
            var windowStart = TimeSpan.Zero;
            long windowBytes = 0;
            var lastUi = TimeSpan.Zero;

            // timeout por leitura: servidor que para de responder não trava o slot para sempre
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            await using (var file = new FileStream(item.TempPath, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
            await using (var net = await response.Content.ReadAsStreamAsync(ct))
            {
                int read;
                while (true)
                {
                    readCts.CancelAfter(TimeSpan.FromSeconds(45));
                    read = await net.ReadAsync(buffer, readCts.Token);
                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    received += read;
                    windowBytes += read;

                    // limite de velocidade: atrasa até a média da janela respeitar o limite
                    if (SpeedLimitKB > 0)
                    {
                        var expected = TimeSpan.FromSeconds((double)windowBytes / (SpeedLimitKB * 1024));
                        var actual = sw.Elapsed - windowStart;
                        if (expected > actual)
                            await Task.Delay(expected - actual, ct);
                    }

                    // atualiza UI a cada 500 ms
                    if (sw.Elapsed - lastUi > TimeSpan.FromMilliseconds(500))
                    {
                        lastUi = sw.Elapsed;
                        var seconds = (sw.Elapsed - windowStart).TotalSeconds;
                        var speed = seconds > 0.1 ? windowBytes / seconds : 0;
                        var r = received;
                        var eta = speed > 0 && total > r
                            ? TimeSpan.FromSeconds((total - r) / speed).ToString(@"hh\:mm\:ss")
                            : "—";
                        UiInvoke(() =>
                        {
                            item.ReceivedBytes = r;
                            item.Progress = total > 0 ? r * 100.0 / total : 0;
                            item.SpeedText = $"{FormatHelper.Bytes((long)speed)}/s";
                            item.EtaText = eta;
                            item.SizeText = $"{FormatHelper.Bytes(r)} / {FormatHelper.Bytes(total)}";
                        });
                        // janela deslizante para a velocidade refletir os últimos segundos
                        if (sw.Elapsed - windowStart > TimeSpan.FromSeconds(8))
                        {
                            windowStart = sw.Elapsed;
                            windowBytes = 0;
                        }
                    }
                }
            }

        return total;
    }

    /// <summary>Descobre o tamanho e se o servidor aceita download em faixas (Range).</summary>
    private async Task<(long Total, bool Ranges)> ProbeAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.PartialContent &&
                response.Content.Headers.ContentRange?.Length is { } length)
                return (length, true);
            return (response.Content.Headers.ContentLength ?? 0, false);
        }
        catch
        {
            return (0, false);
        }
    }

    /// <summary>
    /// Baixa o arquivo em N faixas paralelas (mesma URL), gravando cada uma no seu
    /// offset. O progresso por segmento fica num arquivo .seg ao lado do .part,
    /// então pausar/retomar continua funcionando.
    /// </summary>
    private async Task DownloadSegmentedAsync(DownloadItem item, long total, CancellationToken ct)
    {
        var segCount = Math.Clamp(SegmentsPerDownload, 2, 8);
        var baseSize = total / segCount;
        var segPath = item.TempPath + ".seg";

        // retoma o progresso salvo de cada segmento
        var done = new long[segCount];
        if (File.Exists(segPath))
        {
            var parts = File.ReadAllText(segPath).Split(';');
            if (parts.Length == segCount)
                for (var i = 0; i < segCount; i++)
                    long.TryParse(parts[i], out done[i]);
        }

        using (var pre = new FileStream(item.TempPath, FileMode.OpenOrCreate, FileAccess.Write))
            if (pre.Length != total)
                pre.SetLength(total); // pré-aloca o arquivo completo

        long received = done.Sum();
        UiInvoke(() => item.TotalBytes = total);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sw = Stopwatch.StartNew();

        // monitor: atualiza UI e salva o progresso dos segmentos a cada 500 ms
        var monitor = Task.Run(async () =>
        {
            var windowStart = TimeSpan.Zero;
            var windowBase = Interlocked.Read(ref received);
            while (!linked.Token.IsCancellationRequested)
            {
                try { await Task.Delay(500, linked.Token); } catch { break; }
                var r = Interlocked.Read(ref received);
                var seconds = (sw.Elapsed - windowStart).TotalSeconds;
                var speed = seconds > 0.1 ? (r - windowBase) / seconds : 0;
                var eta = speed > 0 && total > r
                    ? TimeSpan.FromSeconds((total - r) / speed).ToString(@"hh\:mm\:ss")
                    : "—";
                UiInvoke(() =>
                {
                    item.ReceivedBytes = r;
                    item.Progress = r * 100.0 / total;
                    item.SpeedText = $"{FormatHelper.Bytes((long)speed)}/s ({segCount}x)";
                    item.EtaText = eta;
                    item.SizeText = $"{FormatHelper.Bytes(r)} / {FormatHelper.Bytes(total)}";
                });
                try { File.WriteAllText(segPath, string.Join(';', done)); } catch { }
                if (sw.Elapsed - windowStart > TimeSpan.FromSeconds(8))
                {
                    windowStart = sw.Elapsed;
                    windowBase = r;
                }
            }
        }, CancellationToken.None);

        var workers = new List<Task>();
        for (var i = 0; i < segCount; i++)
        {
            var idx = i;
            var start = idx * baseSize;
            var end = idx == segCount - 1 ? total - 1 : start + baseSize - 1;
            workers.Add(Task.Run(async () =>
            {
                var offset = start + done[idx];
                if (offset > end) return; // segmento já concluído

                using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
                request.Headers.Range = new RangeHeaderValue(offset, end);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                if (response.StatusCode != HttpStatusCode.PartialContent)
                    throw new IOException("O servidor recusou o download em partes.");

                await using var net = await response.Content.ReadAsStreamAsync(linked.Token);
                await using var file = new FileStream(item.TempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                file.Seek(offset, SeekOrigin.Begin);

                var buffer = new byte[81920];
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                var segSw = Stopwatch.StartNew();
                long segWindow = 0;
                var segWindowStart = TimeSpan.Zero;
                while (true)
                {
                    readCts.CancelAfter(TimeSpan.FromSeconds(45));
                    var read = await net.ReadAsync(buffer, readCts.Token);
                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), linked.Token);
                    done[idx] += read;
                    Interlocked.Add(ref received, read);

                    // limite de velocidade dividido entre os segmentos
                    if (SpeedLimitKB > 0)
                    {
                        segWindow += read;
                        var share = Math.Max(SpeedLimitKB / segCount, 1);
                        var expected = TimeSpan.FromSeconds((double)segWindow / (share * 1024));
                        var actual = segSw.Elapsed - segWindowStart;
                        if (expected > actual)
                            await Task.Delay(expected - actual, linked.Token);
                        if (segSw.Elapsed - segWindowStart > TimeSpan.FromSeconds(8))
                        {
                            segWindowStart = segSw.Elapsed;
                            segWindow = 0;
                        }
                    }
                }
            }, linked.Token));
        }

        try
        {
            await Task.WhenAll(workers);
        }
        finally
        {
            linked.Cancel();
            try { await monitor; } catch { }
            try { File.WriteAllText(segPath, string.Join(';', done)); } catch { }
        }

        if (done.Sum() < total)
            throw new IOException("Download incompleto — retome para continuar.");
    }

    private void Persist(DownloadItem item) =>
        _db.UpdateDownload(item.DbId, item.Status.ToString(), item.TotalBytes);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* arquivo em uso: ignora */ }
    }
}
