namespace NWXtreamDownloader.Models;

/// <summary>Credenciais de acesso a um servidor Xtream.</summary>
public class XtreamCredentials
{
    public string Server { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>URL base normalizada (sem barra final).</summary>
    public string BaseUrl => Server.TrimEnd('/');

    /// <summary>Extrai as credenciais de uma URL M3U (get.php?username=...&amp;password=...).</summary>
    public static XtreamCredentials FromM3uUrl(string m3uUrl)
    {
        var url = m3uUrl.Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("URL M3U inválida.");

        string username = "", password = "";
        foreach (var p in uri.Query.TrimStart('?').Split('&'))
        {
            var kv = p.Split('=');
            if (kv.Length != 2) continue;
            if (kv[0].Equals("username", StringComparison.OrdinalIgnoreCase))
                username = Uri.UnescapeDataString(kv[1]);
            else if (kv[0].Equals("password", StringComparison.OrdinalIgnoreCase))
                password = Uri.UnescapeDataString(kv[1]);
        }
        if (username.Length == 0 || password.Length == 0)
            throw new ArgumentException("A lista M3U precisa conter usuário e senha.");

        return new XtreamCredentials
        {
            Server = uri.GetLeftPart(UriPartial.Authority),
            Username = username,
            Password = password,
        };
    }
}
