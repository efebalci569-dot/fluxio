namespace Fluxio.Transfer.Security;

/// <summary>Builds paths that are guaranteed to stay directly inside a given folder.</summary>
public static class SafePath
{
    /// <summary>
    /// Combines <paramref name="folder"/> and a single file name. Throws when the result
    /// would escape the folder (traversal, rooted names, alternate data streams, devices).
    /// </summary>
    public static string CombineInside(string folder, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.IndexOfAny(['/', '\\', ':']) >= 0
            || fileName is "." or ".."
            || Path.IsPathRooted(fileName))
        {
            throw new FluxioException(FluxioErrorCode.BadRequest, "Invalid file name.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var full = Path.GetFullPath(Path.Combine(root, fileName));
        var parent = Path.GetDirectoryName(full);

        if (parent is null || !string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new FluxioException(FluxioErrorCode.BadRequest, "File name escapes the target folder.");
        }

        return full;
    }

    /// <summary>Returns "name (n).ext" for the first n that does not exist in the folder.</summary>
    public static string NextFreeName(string folder, string fileName, Func<string, bool>? exists = null)
    {
        exists ??= path => File.Exists(path) || Directory.Exists(path);
        var candidate = CombineInside(folder, fileName);
        if (!exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 1; i < 10_000; i++)
        {
            candidate = CombineInside(folder, $"{stem} ({i}){extension}");
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        throw new FluxioException(FluxioErrorCode.WriteFailed, "Could not find a free file name.");
    }
}
