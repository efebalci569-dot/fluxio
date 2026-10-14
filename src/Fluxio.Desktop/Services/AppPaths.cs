using System.IO;
using System.Runtime.InteropServices;

namespace Fluxio.Desktop.Services;

/// <summary>Where Fluxio keeps its own files. Nothing is written next to the executable.</summary>
public static class AppPaths
{
    /// <summary>%APPDATA%\Fluxio – settings (roams with the user profile).</summary>
    public static string RoamingFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fluxio");

    /// <summary>%LOCALAPPDATA%\Fluxio – history database and logs (machine specific).</summary>
    public static string LocalFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fluxio");

    public static string SettingsFile => Path.Combine(RoamingFolder, "settings.json");

    public static string HistoryDatabase => Path.Combine(LocalFolder, "history.db");

    public static string LogFolder => Path.Combine(LocalFolder, "logs");

    /// <summary>The user's Downloads known folder (it may have been moved to another drive).</summary>
    public static string DownloadsFolder()
    {
        var folderId = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
        if (SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out var pointer) == 0)
        {
            try
            {
                var path = Marshal.PtrToStringUni(pointer);
                if (!string.IsNullOrEmpty(path))
                {
                    return path;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public static string DefaultReceiveFolder() => Path.Combine(DownloadsFolder(), "Fluxio");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);
}
