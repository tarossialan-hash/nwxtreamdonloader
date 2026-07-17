namespace NWXtreamDownloader.Helpers;

/// <summary>Caminhos de dados da aplicação (%AppData%\NWXtreamDownloader).</summary>
public static class AppPaths
{
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NWXtreamDownloader");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string CredentialsFile => Path.Combine(DataDir, "credentials.dat");
    public static string DatabaseFile => Path.Combine(DataDir, "xtream.db");
}
