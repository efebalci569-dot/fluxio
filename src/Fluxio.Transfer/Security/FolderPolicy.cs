namespace Fluxio.Transfer.Security;

/// <summary>
/// Decides which folders may receive files. Operating-system and program folders are
/// refused so a transfer can never replace system or application files.
/// </summary>
public static class FolderPolicy
{
    private static readonly List<string> ExtraProtected = [];

    /// <summary>Adds folders (e.g. the app's own data folder) that must never receive files.</summary>
    public static void Protect(string folder)
    {
        if (!string.IsNullOrWhiteSpace(folder))
        {
            lock (ExtraProtected)
            {
                ExtraProtected.Add(Path.GetFullPath(folder));
            }
        }
    }

    /// <summary>Returns the normalized full path, or throws <see cref="FluxioErrorCode.InvalidFolder"/>.</summary>
    public static string Validate(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            throw new FluxioException(FluxioErrorCode.InvalidFolder, "The folder must be an absolute path.");
        }

        if (folder.StartsWith(@"\\?\", StringComparison.Ordinal) || folder.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            throw new FluxioException(FluxioErrorCode.InvalidFolder, "Device paths are not allowed.");
        }

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FluxioException(FluxioErrorCode.InvalidFolder, "The folder path is not valid.", ex);
        }

        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (!string.IsNullOrEmpty(systemRoot)
            && string.Equals(Path.TrimEndingDirectorySeparator(systemRoot), full, StringComparison.OrdinalIgnoreCase))
        {
            throw new FluxioException(FluxioErrorCode.InvalidFolder, "The root of the system drive is not allowed.");
        }

        foreach (var protectedFolder in ProtectedFolders())
        {
            if (IsSameOrInside(full, protectedFolder))
            {
                throw new FluxioException(FluxioErrorCode.InvalidFolder, "System and program folders are not allowed.");
            }
        }

        return full;
    }

    public static bool IsAllowed(string? folder)
    {
        try
        {
            Validate(folder);
            return true;
        }
        catch (FluxioException)
        {
            return false;
        }
    }

    private static IEnumerable<string> ProtectedFolders()
    {
        Environment.SpecialFolder[] special =
        [
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles,
            Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData,
        ];

        foreach (var item in special)
        {
            var path = Environment.GetFolderPath(item);
            if (!string.IsNullOrEmpty(path))
            {
                yield return Path.TrimEndingDirectorySeparator(path);
            }
        }

        yield return Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        string[] extra;
        lock (ExtraProtected)
        {
            extra = [.. ExtraProtected];
        }

        foreach (var path in extra)
        {
            yield return Path.TrimEndingDirectorySeparator(path);
        }
    }

    private static bool IsSameOrInside(string candidate, string folder) =>
        string.Equals(candidate, folder, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
