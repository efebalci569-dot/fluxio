using System.IO;

namespace Fluxio.Desktop.Services;

/// <summary>Culture-aware formatting for sizes, speeds and durations.</summary>
public static class Fmt
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : value < 10 ? "0.0" : "0";
        return value.ToString(format, Loc.Instance.Culture) + " " + Units[unit];
    }

    public static string Speed(double bytesPerSecond) => Bytes((long)bytesPerSecond) + "/s";

    public static string Duration(TimeSpan? duration)
    {
        if (duration is not { } d)
        {
            return "—";
        }

        if (d.TotalHours >= 1)
        {
            return Loc.Instance.Format("Time.HoursMinutes", (int)d.TotalHours, d.Minutes);
        }

        if (d.TotalMinutes >= 1)
        {
            return Loc.Instance.Format("Time.MinutesSeconds", d.Minutes, d.Seconds);
        }

        return Loc.Instance.Format("Time.Seconds", Math.Max(0, (int)Math.Round(d.TotalSeconds)));
    }

    public static string Countdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        return $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}";
    }

    public static string DateTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("g", Loc.Instance.Culture);

    public static string Time(DateTimeOffset value) =>
        value.ToLocalTime().ToString("t", Loc.Instance.Culture);

    /// <summary>"PDF · Document" style label from the extension (display only, never trusted).</summary>
    public static string FileKind(string name)
    {
        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        var kind = extension switch
        {
            "jpg" or "jpeg" or "png" or "webp" or "gif" or "heic" or "heif" or "bmp" or "tif" or "tiff" or "svg" or "raw" or "dng" => "Kind.Image",
            "mp4" or "mov" or "m4v" or "mkv" or "avi" or "webm" or "3gp" or "wmv" => "Kind.Video",
            "mp3" or "m4a" or "aac" or "wav" or "flac" or "ogg" or "opus" or "wma" => "Kind.Audio",
            "pdf" or "txt" or "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx" or "odt" or "ods" or "odp" or "rtf" or "csv" or "md" or "epub" => "Kind.Document",
            "zip" or "7z" or "rar" or "tar" or "gz" or "bz2" or "xz" or "iso" => "Kind.Archive",
            "cs" or "js" or "ts" or "tsx" or "py" or "java" or "kt" or "c" or "cpp" or "h" or "go" or "rs" or "json" or "xml" or "html" or "css" or "sh" or "ps1" or "sql" or "yml" or "yaml" => "Kind.Code",
            "exe" or "msi" or "bat" or "cmd" or "apk" or "dll" or "scr" or "com" => "Kind.Program",
            _ => "Kind.File",
        };

        var label = Loc.Instance[kind];
        return extension.Length is > 0 and <= 5 ? $"{extension.ToUpperInvariant()} · {label}" : label;
    }
}
