using System.Runtime.InteropServices;
using Fluxio.Transfer.Security;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Transfer.Storage;

/// <summary>
/// Disk side of incoming transfers. Data is streamed into "&lt;target&gt;\.fluxio-incoming\&lt;id&gt;.part"
/// (same volume as the target, so finishing is an atomic rename) and only moved to its final
/// name after the SHA-256 check passed. Partial files are never visible under the real name.
/// </summary>
public static class IncomingStore
{
    public const string TempFolderName = ".fluxio-incoming";
    public const string PartExtension = ".part";

    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);

    public static string EnsureTempFolder(string targetFolder)
    {
        var temp = Path.Combine(targetFolder, TempFolderName);
        var info = Directory.CreateDirectory(temp);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                info.Attributes |= FileAttributes.Hidden;
            }
            catch (IOException)
            {
            }
        }

        return temp;
    }

    public static string PartPathFor(string tempFolder, string fileId) =>
        SafePath.CombineInside(tempFolder, fileId + PartExtension);

    /// <summary>Free bytes on the volume holding <paramref name="folder"/>, or null when unknown.</summary>
    public static long? AvailableFreeSpace(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void EnsureSpace(string folder, long bytesNeeded, long reserve)
    {
        var free = AvailableFreeSpace(folder);
        if (free is { } available && bytesNeeded + reserve > available)
        {
            throw new FluxioException(FluxioErrorCode.InsufficientStorage, "Not enough free disk space.");
        }
    }

    public static bool IsDiskFull(IOException ex) => ex.HResult is ErrorDiskFull or ErrorHandleDiskFull;

    /// <summary>
    /// Moves a verified part file to its final name according to <paramref name="policy"/>.
    /// Returns the final path, or null when the file was skipped because of a name conflict.
    /// </summary>
    public static string? Finalize(string partPath, string targetFolder, string fileName, ConflictPolicy policy)
    {
        var target = SafePath.CombineInside(targetFolder, fileName);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var exists = File.Exists(target) || Directory.Exists(target);
            if (exists)
            {
                switch (policy)
                {
                    case ConflictPolicy.Skip:
                        TryDelete(partPath);
                        return null;
                    case ConflictPolicy.Overwrite when File.Exists(target):
                        File.Move(partPath, target, overwrite: true);
                        return target;
                    default:
                        // Rename, or "overwrite" where a folder has that name: never replace a folder.
                        target = SafePath.NextFreeName(targetFolder, fileName);
                        break;
                }
            }

            try
            {
                File.Move(partPath, target, overwrite: false);
                return target;
            }
            catch (IOException) when (File.Exists(target) && File.Exists(partPath))
            {
                // Someone created the file between the check and the move: decide again.
            }
        }

        throw new FluxioException(FluxioErrorCode.WriteFailed, "Could not store the file under a free name.");
    }

    /// <summary>
    /// Adds the Mark of the Web (Zone.Identifier stream) so Windows SmartScreen and Office treat
    /// the file as coming from another device. Best effort: FAT/exFAT drives have no streams.
    /// </summary>
    public static void MarkAsDownloaded(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
        }
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Removes leftover *.part files (e.g. after a crash) and the temp folder if it is empty.</summary>
    public static int CleanupTempFolder(string targetFolder)
    {
        var temp = Path.Combine(targetFolder, TempFolderName);
        if (!Directory.Exists(temp))
        {
            return 0;
        }

        var removed = 0;
        try
        {
            foreach (var part in Directory.EnumerateFiles(temp, "*" + PartExtension, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(part);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            if (!Directory.EnumerateFileSystemEntries(temp).Any())
            {
                Directory.Delete(temp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return removed;
    }

    public static FluxioException MapWriteError(Exception ex) => ex switch
    {
        IOException io when IsDiskFull(io) => new FluxioException(FluxioErrorCode.InsufficientStorage, "The disk is full.", ex),
        UnauthorizedAccessException => new FluxioException(FluxioErrorCode.PermissionDenied, "Access to the folder was denied.", ex),
        _ => new FluxioException(FluxioErrorCode.WriteFailed, "The file could not be written.", ex),
    };

    internal static bool IsSharingViolation(IOException ex) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (ex.HResult & 0xFFFF) is 32 or 33;
}
