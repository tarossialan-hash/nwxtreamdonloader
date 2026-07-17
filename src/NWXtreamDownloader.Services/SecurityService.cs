using System.Security.Cryptography;
using System.Text;
using NWXtreamDownloader.Database;

namespace NWXtreamDownloader.Services;

/// <summary>
/// Senha de acesso ao aplicativo. Armazenada como hash PBKDF2 (SHA-256,
/// 100.000 iterações, salt aleatório) — nunca em texto puro.
/// </summary>
public class SecurityService
{
    /// <summary>Senha padrão do primeiro acesso (troca obrigatória).</summary>
    public const string DefaultPassword = "lula2026";

    private readonly DatabaseService _db;

    public SecurityService(DatabaseService db) => _db = db;

    /// <summary>true enquanto o usuário ainda não definiu a própria senha.</summary>
    public bool IsFirstAccess => _db.GetSetting("pass_hash") is null;

    /// <summary>Data da última alteração de senha (para a tela de Segurança).</summary>
    public string LastChanged => _db.GetSetting("pass_changed_at") ?? "nunca";

    public bool Verify(string password)
    {
        var stored = _db.GetSetting("pass_hash");
        if (stored is null)
            return password == DefaultPassword;

        var salt = Convert.FromBase64String(_db.GetSetting("pass_salt")!);
        var hash = Hash(password, salt);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(stored));
    }

    public void SetPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        _db.SetSetting("pass_salt", Convert.ToBase64String(salt));
        _db.SetSetting("pass_hash", Convert.ToBase64String(Hash(password, salt)));
        _db.SetSetting("pass_changed_at", DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
    }

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
}
