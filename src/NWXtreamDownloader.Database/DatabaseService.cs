using Microsoft.Data.Sqlite;
using NWXtreamDownloader.Helpers;
using NWXtreamDownloader.Models;

namespace NWXtreamDownloader.Database;

/// <summary>
/// Persistência local em SQLite (%AppData%\NWXtreamDownloader\xtream.db):
/// configurações, downloads pendentes e histórico.
/// </summary>
public class DatabaseService
{
    private readonly string _cs = $"Data Source={AppPaths.DatabaseFile}";

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_cs);
        conn.Open();
        return conn;
    }

    /// <summary>Cria as tabelas na primeira execução.</summary>
    public void Init()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS settings(
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS downloads(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                url TEXT NOT NULL,
                path TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Queued',
                total_bytes INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                completed_at TEXT);
            """;
        cmd.ExecuteNonQuery();

        // migração: coluna de categoria (agrupamento visual) em bancos antigos
        try
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE downloads ADD COLUMN category TEXT NOT NULL DEFAULT ''";
            alter.ExecuteNonQuery();
        }
        catch { /* coluna já existe */ }

        try
        {
            using var alter2 = conn.CreateCommand();
            alter2.CommandText = "ALTER TABLE downloads ADD COLUMN remote_dir TEXT NOT NULL DEFAULT ''";
            alter2.ExecuteNonQuery();
        }
        catch { /* coluna já existe */ }

        using var playlists = conn.CreateCommand();
        playlists.CommandText = """
            CREATE TABLE IF NOT EXISTS playlists(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                url TEXT NOT NULL,
                active INTEGER NOT NULL DEFAULT 1,
                last_sync TEXT,
                movie_count INTEGER NOT NULL DEFAULT 0,
                series_count INTEGER NOT NULL DEFAULT 0);
            """;
        playlists.ExecuteNonQuery();

        try
        {
            using var alter3 = conn.CreateCommand();
            alter3.CommandText = "ALTER TABLE playlists ADD COLUMN expires_at TEXT NOT NULL DEFAULT ''";
            alter3.ExecuteNonQuery();
        }
        catch { /* coluna já existe */ }

        // limpeza: remove listas duplicadas (mesma URL) deixadas por conexões repetidas
        using var dedupe = conn.CreateCommand();
        dedupe.CommandText = "DELETE FROM playlists WHERE id NOT IN (SELECT MIN(id) FROM playlists GROUP BY url)";
        dedupe.ExecuteNonQuery();
    }

    // ---------- Listas M3U ----------

    public List<Playlist> GetPlaylists(bool activeOnly = false)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,name,url,active,last_sync,movie_count,series_count,expires_at FROM playlists" +
                          (activeOnly ? " WHERE active=1" : "") + " ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var list = new List<Playlist>();
        while (reader.Read())
            list.Add(new Playlist
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Url = reader.GetString(2),
                Active = reader.GetInt64(3) == 1,
                LastSync = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                MovieCount = (int)reader.GetInt64(5),
                SeriesCount = (int)reader.GetInt64(6),
                ExpiresAt = reader.GetString(7),
            });
        return list;
    }

    public long InsertPlaylist(string name, string url)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO playlists(name,url) VALUES($n,$u); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$u", url);
        return (long)cmd.ExecuteScalar()!;
    }

    public void UpdatePlaylist(Playlist p)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET name=$n, url=$u, active=$a WHERE id=$id";
        cmd.Parameters.AddWithValue("$n", p.Name);
        cmd.Parameters.AddWithValue("$u", p.Url);
        cmd.Parameters.AddWithValue("$a", p.Active ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", p.Id);
        cmd.ExecuteNonQuery();
    }

    public void UpdatePlaylistSync(long id, int? movieCount, int? seriesCount)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET last_sync=datetime('now','localtime')" +
                          (movieCount is not null ? ", movie_count=$m" : "") +
                          (seriesCount is not null ? ", series_count=$s" : "") + " WHERE id=$id";
        if (movieCount is not null) cmd.Parameters.AddWithValue("$m", movieCount);
        if (seriesCount is not null) cmd.Parameters.AddWithValue("$s", seriesCount);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void UpdatePlaylistExpiry(long id, string expiresAt)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET expires_at=$e WHERE id=$id";
        cmd.Parameters.AddWithValue("$e", expiresAt);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeletePlaylist(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlists WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ---------- Configurações ----------

    public string? GetSetting(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    // ---------- Downloads ----------

    public long InsertDownload(string title, string url, string path, string category, string remoteDir = "")
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO downloads(title,url,path,category,remote_dir) VALUES($t,$u,$p,$c,$r); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$u", url);
        cmd.Parameters.AddWithValue("$p", path);
        cmd.Parameters.AddWithValue("$c", category);
        cmd.Parameters.AddWithValue("$r", remoteDir);
        return (long)cmd.ExecuteScalar()!;
    }

    public void UpdateDownload(long id, string status, long totalBytes, bool completed = false)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = completed
            ? "UPDATE downloads SET status=$s, total_bytes=$b, completed_at=datetime('now','localtime') WHERE id=$id"
            : "UPDATE downloads SET status=$s, total_bytes=$b WHERE id=$id";
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$b", totalBytes);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteDownload(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM downloads WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void ClearHistory()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM downloads WHERE status='Completed' OR status='Canceled'";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Downloads pendentes (retomados na abertura) ou histórico completo.</summary>
    public List<DownloadRecord> GetDownloads(bool pendingOnly)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = pendingOnly
            ? "SELECT id,title,url,path,status,total_bytes,created_at,completed_at,category,remote_dir FROM downloads WHERE status NOT IN ('Completed','Canceled') ORDER BY id"
            : "SELECT id,title,url,path,status,total_bytes,created_at,completed_at,category,remote_dir FROM downloads WHERE status='Completed' ORDER BY completed_at DESC";
        using var reader = cmd.ExecuteReader();
        var list = new List<DownloadRecord>();
        while (reader.Read())
            list.Add(new DownloadRecord
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Url = reader.GetString(2),
                Path = reader.GetString(3),
                Status = reader.GetString(4),
                TotalBytes = reader.GetInt64(5),
                CreatedAt = DateTime.Parse(reader.GetString(6)),
                CompletedAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
                Category = reader.GetString(8),
                RemoteDir = reader.GetString(9),
            });
        return list;
    }

    /// <summary>Verifica se um caminho já consta como concluído no histórico.</summary>
    public bool IsCompleted(string path)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM downloads WHERE path=$p AND status='Completed'";
        cmd.Parameters.AddWithValue("$p", path);
        return (long)cmd.ExecuteScalar()! > 0;
    }
}
