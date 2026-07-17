using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.Database;
using NWXtreamDownloader.Helpers;

namespace NWXtreamDownloader.ViewModels;

/// <summary>Entrada do histórico formatada para exibição.</summary>
public class HistoryEntry
{
    public string Title { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

/// <summary>Histórico de downloads concluídos (SQLite).</summary>
public partial class HistoryViewModel : ViewModelBase
{
    private readonly DatabaseService _db;

    public ObservableCollection<HistoryEntry> Entries { get; } = [];

    public HistoryViewModel(DatabaseService db) => _db = db;

    [RelayCommand]
    public void Refresh()
    {
        Entries.Clear();
        foreach (var r in _db.GetDownloads(pendingOnly: false))
            Entries.Add(new HistoryEntry
            {
                Title = r.Title,
                Date = r.CompletedAt?.ToString("dd/MM/yyyy HH:mm") ?? "",
                Size = FormatHelper.Bytes(r.TotalBytes),
                Path = r.Path,
            });
    }

    [RelayCommand]
    public void Clear()
    {
        _db.ClearHistory();
        Refresh();
    }
}
