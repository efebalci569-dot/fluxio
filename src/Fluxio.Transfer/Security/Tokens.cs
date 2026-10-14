using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Fluxio.Transfer.Security;

/// <summary>Cryptographically secure identifiers, secrets and comparisons.</summary>
public static class Tokens
{
    /// <summary>Returns <paramref name="bytes"/> random bytes from the OS CSPRNG encoded as base64url (no padding).</summary>
    public static string NewToken(int bytes = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, 16);
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));
    }

    /// <summary>A short random id (96 bits) for batches and files; not a secret, only unguessable.</summary>
    public static string NewId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(12));

    /// <summary>A 4-digit code shown on both screens so the user can confirm they approve the right phone.</summary>
    public static string NewMatchCode() => RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");

    /// <summary>SHA-256 of a token, used so raw bearer tokens are never stored.</summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Constant-time comparison for secrets (length is not secret).</summary>
    public static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }

    /// <summary>True for a 64 character lowercase/uppercase hex string.</summary>
    public static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
