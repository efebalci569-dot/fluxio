using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Fluxio.Desktop.Services;

/// <summary>
/// Windows notifications through a notification-area icon (shown as toasts on Windows 10/11)
/// plus taskbar flashing. Used only when the Fluxio window is not in the foreground and the
/// user has notifications enabled.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly SettingsService _settings;

    public NotificationService(SettingsService settings)
    {
        _settings = settings;
        _icon = new Forms.NotifyIcon
        {
            Text = "Fluxio",
            Visible = true,
        };

        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Fluxio;component/Resources/fluxio.ico"))?.Stream;
            if (stream is not null)
            {
                _icon.Icon = new System.Drawing.Icon(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
        }

        _icon.Click += (_, _) => ShowWindow();
        _icon.BalloonTipClicked += (_, _) => ShowWindow();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Fluxio", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.Instance["Tray.Exit"], null, (_, _) => Application.Current.Dispatcher.BeginInvoke(() => Application.Current.MainWindow?.Close()));
        _icon.ContextMenuStrip = menu;
        Loc.Instance.LanguageChanged += (_, _) => menu.Items[2].Text = Loc.Instance["Tray.Exit"];
    }

    /// <summary>Shows a Windows notification (if enabled and Fluxio is not the active window).</summary>
    public void Notify(string title, string message, bool important = false)
    {
        var window = Application.Current.MainWindow;
        if (window is null || window.IsActive)
        {
            return;
        }

        if (important)
        {
            Flash(window);
        }

        if (_settings.Current.Notifications)
        {
            _icon.ShowBalloonTip(5000, title, message, important ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.None);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static void ShowWindow()
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var window = Application.Current.MainWindow;
            if (window is null)
            {
                return;
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Show();
            window.Activate();
        });
    }

    private static void Flash(Window window)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Hwnd = new WindowInteropHelper(window).Handle,
            Flags = 0x3 | 0xC, // FLASHW_ALL | FLASHW_TIMERNOFG
            Count = 5,
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
