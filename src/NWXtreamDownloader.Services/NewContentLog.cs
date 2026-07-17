using System.Collections.ObjectModel;

namespace NWXtreamDownloader.Services;

/// <summary>
/// Registro em memória dos conteúdos ENVIADOS ao servidor FTP nesta sessão
/// (aba Novos Conteúdos). Só entra na lista após o upload 100% concluído.
/// Sempre vazio ao abrir o app.
/// </summary>
public class NewContentLog
{
    public ObservableCollection<string> Entries { get; } = [];

    /// <summary>Marshaling para a thread de UI (definido pelo App).</summary>
    public Action<Action> UiInvoke { get; set; } = a => a();

    // uma linha por série, atualizada conforme cada episódio termina de subir
    private readonly Dictionary<string, int> _seriesLine = [];
    private readonly Dictionary<string, SortedDictionary<int, int>> _seriesSeasons = [];

    public void AddMovie(string ftpFolder, string name) =>
        UiInvoke(() => Entries.Add($"[{ftpFolder}] {name}"));

    public void AddUploadedEpisode(string ftpFolder, string seriesLabel, int season)
    {
        UiInvoke(() =>
        {
            var key = $"{ftpFolder}|{seriesLabel}";
            if (!_seriesSeasons.TryGetValue(key, out var seasons))
            {
                seasons = [];
                _seriesSeasons[key] = seasons;
                Entries.Add(string.Empty);
                _seriesLine[key] = Entries.Count - 1;
            }
            seasons[season] = seasons.GetValueOrDefault(season) + 1;
            var summary = string.Join(", ", seasons.Select(kv => $"temp {kv.Key} ({kv.Value} ep.)"));
            Entries[_seriesLine[key]] = $"[{ftpFolder}] {seriesLabel} - {summary}";
        });
    }

    public void Clear() => UiInvoke(() =>
    {
        Entries.Clear();
        _seriesLine.Clear();
        _seriesSeasons.Clear();
    });

    public string ToText() => string.Join(Environment.NewLine, Entries);
}
