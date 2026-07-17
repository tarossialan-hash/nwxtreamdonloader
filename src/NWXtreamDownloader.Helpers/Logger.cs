namespace NWXtreamDownloader.Helpers;

/// <summary>Log simples em arquivo (%AppData%\NWXtreamDownloader\app.log).</summary>
public static class Logger
{
    private static readonly object Lock = new();

    public static string LogFile => Path.Combine(AppPaths.DataDir, "app.log");

    public static void Error(string context, Exception? ex)
    {
        try
        {
            lock (Lock)
                File.AppendAllText(LogFile, $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss}] {context}: {ex}\n\n");
        }
        catch { /* nunca derrubar o app por falha de log */ }
    }
}
