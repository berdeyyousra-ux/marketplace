using System.Security.Cryptography;
using System.Text;

namespace Marketplace.Application.Common;

// D-17: tokens are 32 random bytes in base64url, and only their SHA-256 hash (64 lowercase hex characters) is stored.
public static class SecureTokens
{
    public static (string Raw, string Hash) Generate()
    {
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    public static string Hash(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
