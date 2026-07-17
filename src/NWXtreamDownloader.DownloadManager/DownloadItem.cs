using CommunityToolkit.Mvvm.ComponentModel;

namespace NWXtreamDownloader.DownloadManager;

public enum DownloadStatus { Queued, Downloading, Paused, Completed, Failed, Canceled }

/// <summary>Item da fila de downloads, com propriedades observáveis para binding direto na UI.</summary>
public partial class DownloadItem : ObservableObject
{
    public long DbId { get; set; }
    public string Title { get; init; } = string.Empty;
    /// <summary>Agrupamento visual: "Filmes" ou o nome da série.</summary>
    public string Category { get; init; } = string.Empty;
    /// <summary>Pasta remota escolhida para o envio SFTP ("" = padrão das configurações).</summary>
    public string RemoteDir { get; set; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string DestinationPath { get; init; } = string.Empty;
    /// <summary>Arquivo temporário usado durante o download (permite retomar).</summary>
    public string TempPath => DestinationPath + ".part";

    internal CancellationTokenSource? Cts;
    internal bool PauseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    private DownloadStatus _status = DownloadStatus.Queued;

    public bool IsCompleted => Status == DownloadStatus.Completed;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private long _receivedBytes;

    [ObservableProperty]
    private string _speedText = "";

    [ObservableProperty]
    private string _etaText = "";

    [ObservableProperty]
    private string _sizeText = "";

    /// <summary>Status do envio SFTP pós-download (vazio se envio desativado).</summary>
    [ObservableProperty]
    private string _uploadText = "";

    public string StatusText => Status switch
    {
        DownloadStatus.Queued => "Na fila",
        DownloadStatus.Downloading => "Baixando",
        DownloadStatus.Paused => "Pausado",
        DownloadStatus.Completed => "Concluído",
        DownloadStatus.Failed => "Falhou",
        _ => "Cancelado",
    };
}
