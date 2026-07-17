namespace NWXtreamDownloader.Models;

/// <summary>Categoria de conteúdo (filmes ou séries).</summary>
public class XtreamCategory
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Quantidade de itens (calculada localmente).</summary>
    public int Count { get; set; }

    public override string ToString() => Count > 0 ? $"{Name} ({Count})" : Name;
}
