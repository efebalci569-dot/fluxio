using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Fluxio.Desktop.Services;

/// <summary>Native Windows dialogs and shell actions used by the view models.</summary>
public static class Dialogs
{
    public static IReadOnlyList<string> PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = Loc.Instance["Send.AddFiles"],
            CheckFileExists = true,
            DereferenceLinks = true,
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileNames : [];
    }

    public static string? PickFolder(string? initial, string titleKey)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.Instance[titleKey],
            Multiselect = false,
        };
        if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial))
        {
            dialog.InitialDirectory = initial;
        }

        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    public static bool Confirm(string title, string message)
    {
        var owner = Application.Current.MainWindow;
        return MessageBox.Show(owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public static void Error(string title, string message)
    {
        var owner = Application.Current.MainWindow;
        if (owner is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Opens a folder in File Explorer (and selects a file when given). Never runs the file.</summary>
    public static void ShowInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
        }
        catch (Win32Exception)
        {
        }
    }

    public static void OpenUrl(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
        }
    }
}
