using NWXtreamDownloader.Database;
using NWXtreamDownloader.Helpers;

namespace NWXtreamDownloader.Services;

/// <summary>Configurações do usuário persistidas no SQLite.</summary>
public class SettingsService
{
    private readonly DatabaseService _db;

    public SettingsService(DatabaseService db) => _db = db;

    /// <summary>User-Agent padrão (Chrome no Windows) — alguns painéis IPTV bloqueiam clientes sem ele.</summary>
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    /// <summary>Disparado ao trocar o tema ("Dark"/"Light") para o App aplicar imediatamente.</summary>
    public event Action<string>? ThemeChanged;

    /// <summary>Disparado ao trocar o User-Agent para reaplicar no HttpClient.</summary>
    public event Action<string>? UserAgentChanged;

    public string UserAgent
    {
        get
        {
            var ua = _db.GetSetting("user_agent");
            return string.IsNullOrWhiteSpace(ua) ? DefaultUserAgent : ua;
        }
        set
        {
            var ua = string.IsNullOrWhiteSpace(value) ? DefaultUserAgent : value.Trim();
            _db.SetSetting("user_agent", ua);
            UserAgentChanged?.Invoke(ua);
        }
    }

    public string DownloadFolder
    {
        get => _db.GetSetting("download_folder")
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        set => _db.SetSetting("download_folder", value);
    }

    public int MaxConcurrent
    {
        get => int.TryParse(_db.GetSetting("max_concurrent"), out var v) ? Math.Clamp(v, 1, 10) : 2;
        set => _db.SetSetting("max_concurrent", value.ToString());
    }

    /// <summary>Conexões paralelas por arquivo (download segmentado, 1 = desligado).</summary>
    public int SegmentsPerDownload
    {
        get => int.TryParse(_db.GetSetting("segments"), out var v) ? Math.Clamp(v, 1, 8) : 4;
        set => _db.SetSetting("segments", value.ToString());
    }

    /// <summary>Limite de velocidade por download em KB/s (0 = ilimitado).</summary>
    public int SpeedLimitKB
    {
        get => int.TryParse(_db.GetSetting("speed_limit_kb"), out var v) ? Math.Max(0, v) : 0;
        set => _db.SetSetting("speed_limit_kb", value.ToString());
    }

    public string Language
    {
        get => _db.GetSetting("language") ?? "Português";
        set => _db.SetSetting("language", value);
    }

    public string Theme
    {
        get => _db.GetSetting("theme") ?? "Dark";
        set
        {
            _db.SetSetting("theme", value);
            ThemeChanged?.Invoke(value);
        }
    }

    public bool SaveCredentials
    {
        get => _db.GetSetting("save_credentials") != "0";
        set => _db.SetSetting("save_credentials", value ? "1" : "0");
    }

    /// <summary>Substituir arquivos já existentes ao baixar novamente.</summary>
    public bool OverwriteExisting
    {
        get => _db.GetSetting("overwrite_existing") == "1";
        set => _db.SetSetting("overwrite_existing", value ? "1" : "0");
    }

    /// <summary>Última lista M3U aberta (0 = nenhuma) — o app entra direto nela ao abrir.</summary>
    public long LastPlaylistId
    {
        get => long.TryParse(_db.GetSetting("last_playlist"), out var v) ? v : 0;
        set => _db.SetSetting("last_playlist", value.ToString());
    }

    /// <summary>Padrão da pasta de temporada: {n} = número, {nn} = com zero à esquerda.</summary>
    public string SeasonFolderPattern
    {
        get => _db.GetSetting("season_pattern") ?? "temp {n}";
        set => _db.SetSetting("season_pattern", string.IsNullOrWhiteSpace(value) ? "temp {n}" : value.Trim());
    }

    // ---------- SFTP (envio automático para o servidor) ----------

    public bool SftpEnabled
    {
        get => _db.GetSetting("sftp_enabled") == "1";
        set => _db.SetSetting("sftp_enabled", value ? "1" : "0");
    }

    public string SftpHost
    {
        get => _db.GetSetting("sftp_host") ?? "";
        set => _db.SetSetting("sftp_host", value.Trim());
    }

    public int SftpPort
    {
        get => int.TryParse(_db.GetSetting("sftp_port"), out var v) && v > 0 ? v : 22;
        set => _db.SetSetting("sftp_port", value.ToString());
    }

    public string SftpUser
    {
        get => _db.GetSetting("sftp_user") ?? "";
        set => _db.SetSetting("sftp_user", value.Trim());
    }

    /// <summary>Senha do SFTP criptografada com DPAPI (nunca em texto puro).</summary>
    public string SftpPassword
    {
        get
        {
            try
            {
                var v = _db.GetSetting("sftp_password");
                return string.IsNullOrEmpty(v) ? "" : CryptoHelper.Unprotect(Convert.FromBase64String(v));
            }
            catch { return ""; }
        }
        set => _db.SetSetting("sftp_password", Convert.ToBase64String(CryptoHelper.Protect(value)));
    }

    /// <summary>Envios SFTP simultâneos (1 a 4).</summary>
    public int UploadConcurrency
    {
        get => int.TryParse(_db.GetSetting("upload_concurrency"), out var v) ? Math.Clamp(v, 1, 4) : 2;
        set => _db.SetSetting("upload_concurrency", value.ToString());
    }

    /// <summary>Pasta remota base (ex.: /home/user/VODS). A estrutura Filmes/Séries é replicada dentro dela.</summary>
    public string SftpRemoteDir
    {
        get => _db.GetSetting("sftp_remote_dir") ?? "/";
        set => _db.SetSetting("sftp_remote_dir", string.IsNullOrWhiteSpace(value) ? "/" : value.Trim());
    }
}
