using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NWXtreamDownloader.Services;

namespace NWXtreamDownloader.UI.Views;

/// <summary>Diálogo de escolha da pasta remota (SFTP) na hora do download.</summary>
public partial class RemoteFolderPickerWindow : Window
{
    private readonly SftpUploadService _sftp = null!;
    private string _path = "/";
    private bool _navigating;

    /// <summary>Pasta escolhida (válida quando o diálogo retorna true).</summary>
    public string? SelectedPath { get; private set; }

    public RemoteFolderPickerWindow()
    {
        InitializeComponent(); // construtor exigido pelo XAML loader
    }

    public RemoteFolderPickerWindow(SftpUploadService sftp, string startPath) : this()
    {
        _sftp = sftp;
        _path = string.IsNullOrWhiteSpace(startPath) ? "/" : startPath;
        _ = LoadAsync();
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = LoadAsync(forceRefresh: true);

    private async System.Threading.Tasks.Task LoadAsync(bool forceRefresh = false)
    {
        PathBox.Text = _path;
        StatusText.Text = "Carregando pastas...";
        try
        {
            var dirs = await _sftp.ListDirectoriesAsync(_path, forceRefresh);
            var items = new List<string>();
            if (_path.TrimEnd('/').Length > 0)
                items.Add("‹ voltar");
            items.AddRange(dirs);
            _navigating = true;
            FolderList.ItemsSource = items;
            FolderList.SelectedItem = null;
            _navigating = false;
            StatusText.Text = $"{dirs.Count} pasta(s) — clique para entrar; \"Usar esta pasta\" confirma a atual.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Erro ao listar: {ex.Message}";
        }
    }

    private void OnFolderSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_navigating || FolderList.SelectedItem is not string chosen) return;
        var p = _path.TrimEnd('/');
        _path = chosen == "‹ voltar"
            ? (p.LastIndexOf('/') <= 0 ? "/" : p[..p.LastIndexOf('/')])
            : chosen;
        _ = LoadAsync();
    }

    private async void OnCreateFolder(object? sender, RoutedEventArgs e)
    {
        var name = NewFolderBox.Text?.Trim() ?? "";
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\'))
        {
            StatusText.Text = "Digite um nome de pasta válido.";
            return;
        }
        try
        {
            await _sftp.CreateDirectoryAsync(_path.TrimEnd('/') + "/" + name);
            NewFolderBox.Text = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Erro ao criar: {ex.Message}";
        }
    }

    private void OnUse(object? sender, RoutedEventArgs e)
    {
        SelectedPath = _path;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
