using System.Security.Cryptography;
using System.Text;

namespace NWXtreamDownloader.Helpers;

/// <summary>Criptografia de dados locais via DPAPI (vinculada ao usuário do Windows).</summary>
public static class CryptoHelper
{
    public static byte[] Protect(string plainText) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), null, DataProtectionScope.CurrentUser);

    public static string Unprotect(byte[] encrypted) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
}
