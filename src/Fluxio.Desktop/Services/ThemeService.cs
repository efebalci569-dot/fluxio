using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Fluxio.Desktop.Services;

/// <summary>Switches the Dark/Light resource dictionaries and keeps the window title bar in sync.</summary>
public sealed class ThemeService
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private ThemeChoice _choice = ThemeChoice.Dark;

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _choice == ThemeChoice.System)
            {
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(_choice));
            }
        };
    }

    public bool IsDark { get; private set; } = true;

    public event EventHandler? ThemeChanged;

    public void Apply(ThemeChoice choice)
    {
        _choice = choice;
        IsDark = choice switch
        {
            ThemeChoice.Light => false,
            ThemeChoice.Dark => true,
            _ => !SystemUsesLightTheme(),
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var source = new Uri($"pack://application:,,,/Fluxio;component/Resources/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute);
        var existing = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Resources/Themes/", StringComparison.Ordinal) == true);
        var replacement = new ResourceDictionary { Source = source };
        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Dark/light native title bar (Windows 10 2004+) and matching caption color (Windows 11).</summary>
    public void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        if (Application.Current.TryFindResource("Color.Sidebar") is Color color)
        {
            var colorRef = color.R | (color.G << 8) | (color.B << 16);
            _ = DwmSetWindowAttribute(handle, DwmCaptionColor, ref colorRef, sizeof(int));
        }
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
