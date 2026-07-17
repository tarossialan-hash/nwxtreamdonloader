using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.Database;
using NWXtreamDownloader.Models;
using NWXtreamDownloader.Services;

namespace NWXtreamDownloader.ViewModels;

/// <summary>Aba Listas: gerenciamento das listas M3U (adicionar, editar, ativar, remover).</summary>
public partial class PlaylistsViewModel : ViewModelBase
{
    private readonly DatabaseService _db;
    private readonly CatalogService _catalog;

    public ObservableCollection<Playlist> Lists { get; } = [];

    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editUrl = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Id da lista em edição (null = adicionando nova).</summary>
    private long? _editingId;

    /// <summary>Disparado quando as listas mudam (Filmes/Séries atualizam o seletor).</summary>
    public event Action? ListsChanged;

    public PlaylistsViewModel(DatabaseService db, CatalogService catalog)
    {
        _db = db;
        _catalog = catalog;
        Refresh();
    }

    public void Refresh()
    {
        Lists.Clear();
        foreach (var p in _db.GetPlaylists())
            Lists.Add(p);
    }

    [RelayCommand]
    private void Save()
    {
        var name = EditName.Trim();
        var url = EditUrl.Trim();
        if (name.Length == 0 || url.Length == 0)
        {
            StatusMessage = "Preencha o nome e a URL M3U da lista.";
            return;
        }
        try
        {
            XtreamCredentials.FromM3uUrl(url); // valida o formato
        }
        catch (Exception ex)
        {
            StatusMessage = $"✖ {ex.Message}";
            return;
        }

        if (_editingId is { } id)
        {
            var p = Lists.First(l => l.Id == id);
            p.Name = name;
            p.Url = url;
            _db.UpdatePlaylist(p);
            _catalog.Invalidate(id); // URL pode ter mudado: recarrega na próxima vez
            StatusMessage = $"Lista \"{name}\" atualizada.";
        }
        else
        {
            _db.InsertPlaylist(name, url);
            StatusMessage = $"Lista \"{name}\" adicionada.";
        }
        _editingId = null;
        EditName = EditUrl = string.Empty;
        Refresh();
        ListsChanged?.Invoke();
    }

    /// <summary>Sincroniza filmes, séries e vencimento de uma lista (botão por item).</summary>
    public async System.Threading.Tasks.Task SyncAsync(Playlist p)
    {
        StatusMessage = $"Sincronizando \"{p.Name}\"...";
        try
        {
            var info = await _catalog.ConnectAsync(p);
            _db.UpdatePlaylistExpiry(p.Id, info.ExpiresAt?.ToString("dd/MM/yyyy") ?? "");
            await _catalog.GetMoviesAsync(p, forceRefresh: true);
            await _catalog.GetSeriesAsync(p, forceRefresh: true);
            StatusMessage = $"\"{p.Name}\" sincronizada.";
            Refresh();
            ListsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"✖ {ex.Message}";
        }
    }

    public void BeginEdit(Playlist p)
    {
        _editingId = p.Id;
        EditName = p.Name;
        EditUrl = p.Url;
        StatusMessage = $"Editando \"{p.Name}\" — altere e clique em Salvar.";
    }

    public void ToggleActive(Playlist p)
    {
        p.Active = !p.Active;
        _db.UpdatePlaylist(p);
        Refresh();
        ListsChanged?.Invoke();
    }

    public void Delete(Playlist p)
    {
        _db.DeletePlaylist(p.Id);
        _catalog.Invalidate(p.Id);
        StatusMessage = $"Lista \"{p.Name}\" removida.";
        Refresh();
        ListsChanged?.Invoke();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _editingId = null;
        EditName = EditUrl = string.Empty;
        StatusMessage = string.Empty;
    }
}
