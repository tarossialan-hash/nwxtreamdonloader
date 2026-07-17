using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using NWXtreamDownloader.DownloadManager;

namespace NWXtreamDownloader.ViewModels;

/// <summary>Tela de Downloads: fila com pausa, retomada, cancelamento e remoção.</summary>
public partial class DownloadsViewModel : ViewModelBase
{
    public DownloadService Service { get; }
    private readonly NWXtreamDownloader.Services.SftpUploadService _sftp;
    public System.Collections.ObjectModel.ObservableCollection<DownloadGroup> GroupedItems { get; } = [];

    public DownloadsViewModel(DownloadService service, NWXtreamDownloader.Services.SftpUploadService sftp) 
    {
        Service = service;
        _sftp = sftp;
        Service.Items.CollectionChanged += OnItemsChanged;
        foreach (var item in Service.Items) AddToGroup(item, isNew: false);
    }

    private void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (DownloadItem item in e.NewItems) AddToGroup(item, isNew: true);
        if (e.OldItems != null) foreach (DownloadItem item in e.OldItems) RemoveFromGroup(item);
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            GroupedItems.Clear();
            foreach (var item in Service.Items) AddToGroup(item, isNew: false);
        }
    }

    private void AddToGroup(DownloadItem item, bool isNew)
    {
        var group = GroupedItems.FirstOrDefault(g => g.Category == item.Category);
        if (group == null)
        {
            group = new DownloadGroup(item.Category) { IsExpanded = isNew };
            GroupedItems.Add(group);
        }
        else if (isNew)
        {
            group.IsExpanded = true;
        }
        group.Items.Add(item);
        item.PropertyChanged += Item_PropertyChanged;
    }

    private void RemoveFromGroup(DownloadItem item)
    {
        var group = GroupedItems.FirstOrDefault(g => g.Category == item.Category);
        if (group != null)
        {
            group.Items.Remove(item);
            if (group.Items.Count == 0) GroupedItems.Remove(group);
        }
        item.PropertyChanged -= Item_PropertyChanged;
    }

    private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.UploadText))
        {
            if (sender is DownloadItem item)
            {
                var group = GroupedItems.FirstOrDefault(g => g.Category == item.Category);
                group?.UpdateStats();
            }
        }
    }

    [RelayCommand] private void Pause(DownloadItem item) => Service.Pause(item);
    [RelayCommand] private void Resume(DownloadItem item) => Service.Resume(item);
    [RelayCommand] private void Cancel(DownloadItem item) => Service.Cancel(item);
    [RelayCommand] private void Remove(DownloadItem item) => Service.Remove(item);
    [RelayCommand] private void ForceUpload(DownloadItem item) => _ = _sftp.UploadAsync(item, force: true);

    /// <summary>Envia todos os concluídos que ainda não subiram (fila de 1 por vez no serviço).</summary>
    [RelayCommand]
    private void UploadCompleted()
    {
        foreach (var item in Service.Items.Where(i =>
                     i.Status == DownloadStatus.Completed && i.UploadText?.Contains('✔') != true).ToList())
            _ = _sftp.UploadAsync(item, force: true);
    }

    [RelayCommand] private void StartGroup(DownloadGroup group)
    {
        foreach (var item in group.Items.ToList()) Service.Resume(item);
    }
    [RelayCommand] private void PauseGroup(DownloadGroup group)
    {
        foreach (var item in group.Items.ToList()) Service.Pause(item);
    }
    [RelayCommand] private void CancelGroup(DownloadGroup group)
    {
        foreach (var item in group.Items.ToList()) Service.Cancel(item);
    }

    /// <summary>Remove o grupo inteiro da lista (cancela os ativos).</summary>
    [RelayCommand] private void RemoveGroup(DownloadGroup group)
    {
        foreach (var item in group.Items.ToList()) Service.Remove(item);
    }

    [RelayCommand] private void StartAll() => Service.StartAll();
    [RelayCommand] private void PauseAll() => Service.PauseAll();
    [RelayCommand] private void CancelAll() => Service.CancelAll();
    [RelayCommand] private void ClearAll() => Service.ClearAll();
}

public partial class DownloadGroup : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Category { get; }
    public System.Collections.ObjectModel.ObservableCollection<DownloadItem> Items { get; } = [];

    public int Total => Items.Count;
    public int Completed => Items.Count(i => i.Status == DownloadStatus.Completed);
    public int Uploaded => Items.Count(i => i.UploadText?.Contains("✔") == true);
    
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isExpanded;
    
    public DownloadGroup(string category)
    {
        Category = category;
        Items.CollectionChanged += (s, e) => UpdateStats();
    }
    
    public void UpdateStats()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Completed));
        OnPropertyChanged(nameof(Uploaded));
    }
}
