namespace NWXtreamDownloader.Helpers;

/// <summary>Formatação de valores para exibição.</summary>
public static class FormatHelper
{
    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.##} {units[i]}";
    }
}
