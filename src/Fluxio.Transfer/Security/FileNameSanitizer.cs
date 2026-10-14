using System.Text;

namespace Fluxio.Transfer.Security;

/// <summary>
/// Turns a file name supplied by a remote device into a safe single Windows file name.
/// Directory parts are dropped, so the result can never point outside the target folder.
/// The rules are Windows rules on every OS so behaviour is identical in tests and CI.
/// </summary>
public static class FileNameSanitizer
{
    public const int MaxLength = 180;
    public const string Fallback = "file";

    private static readonly HashSet<char> Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        string normalized;
        try
        {
            normalized = name.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // Invalid UTF-16 (lone surrogates): fall back to the raw string, cleaned below.
            normalized = name;
        }

        // Keep only the last path segment, whatever separator the client used.
        var lastSeparator = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
        if (lastSeparator >= 0)
        {
            normalized = normalized[(lastSeparator + 1)..];
        }

        var builder = new StringBuilder(normalized.Length);
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (char.IsHighSurrogate(c) && i + 1 < normalized.Length && char.IsLowSurrogate(normalized[i + 1]))
            {
                builder.Append(c).Append(normalized[i + 1]);
                i++;
                continue;
            }

            builder.Append(IsUnsafe(c) ? '_' : c);
        }

        // Windows silently strips trailing dots and spaces, which would let "a.txt." collide
        // with "a.txt"; names made only of dots ("." / "..") collapse to empty here.
        var result = builder.ToString().Trim().TrimEnd('.', ' ');
        if (result.Length == 0)
        {
            return Fallback;
        }

        var stem = result.Split('.')[0].TrimEnd(' ');
        if (ReservedNames.Contains(stem))
        {
            result = "_" + result;
        }

        if (result.Length > MaxLength)
        {
            result = Truncate(result);
        }

        return result;
    }

    private static bool IsUnsafe(char c) =>
        c < 0x20
        || c == 0x7F
        || Invalid.Contains(c)
        || char.IsSurrogate(c) // lone surrogate
        || c is '‎' or '‏' or '؜' // directional marks
        || c is >= '‪' and <= '‮' // embedding / override (e.g. "gpj.exe" spoofing)
        || c is >= '⁦' and <= '⁩'; // isolates

    private static string Truncate(string name)
    {
        var extension = Path.GetExtension(name);
        if (extension.Length is 0 or > 16)
        {
            extension = string.Empty;
        }

        var stemLength = MaxLength - extension.Length;
        var stem = name[..stemLength];
        if (char.IsHighSurrogate(stem[^1]))
        {
            stem = stem[..^1];
        }

        stem = stem.TrimEnd('.', ' ');
        return (stem.Length == 0 ? Fallback : stem) + extension;
    }
}
