using System.IO;
using System.Windows;
using System.Windows.Threading;
using Fluxio.Desktop.Services;
using Fluxio.Desktop.ViewModels;
using Fluxio.Desktop.Views;
using Fluxio.Transfer.Security;
using Fluxio.Transfer.Sessions;
using Fluxio.Transfer.Storage;
using Microsoft.Extensions.Logging;

namespace Fluxio.Desktop;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\Fluxio.Desktop.SingleInstance";
    private Mutex? _instanceMutex;
    private AppServices? _services;
    private ILoggerFactory? _loggerFactory;

    protected override void OnStartup(StartupEventArgs e)
    {
        // One Fluxio per user: a second copy would compete for the port and the download folder.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Fluxio is already running.", "Fluxio", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Unobserved task exception", args.Exception.InnerException ?? args.Exception);
            args.SetObserved();
        };

        AppLog.Info($"Fluxio {AppServices.Version} starting");
        FolderPolicy.Protect(AppPaths.LocalFolder);
        FolderPolicy.Protect(AppPaths.RoamingFolder);

        var settings = new SettingsService();
        Loc.Instance.SetLanguage(settings.Current.Language);
        var theme = new ThemeService();
        theme.Apply(settings.Current.Theme);

        HistoryStore history;
        try
        {
            history = new HistoryStore(AppPaths.HistoryDatabase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            // A broken history database must not stop transfers: start a fresh one.
            AppLog.Error("History database could not be opened; starting a new one", ex);
            var broken = AppPaths.HistoryDatabase + $".broken-{DateTime.Now:yyyyMMddHHmmss}";
            try
            {
                File.Move(AppPaths.HistoryDatabase, broken);
            }
            catch (IOException)
            {
            }

            history = new HistoryStore(AppPaths.HistoryDatabase);
        }

        // Leftover partial files from an earlier crash are never valid: remove them.
        var removed = IncomingStore.CleanupTempFolder(settings.Current.DownloadFolder);
        if (removed > 0)
        {
            AppLog.Info($"Removed {removed} leftover partial file(s)");
        }

        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new AppLoggerProvider()).SetMinimumLevel(LogLevel.Information));
        var sessions = new SessionManager(settings.Current.ToTransferOptions(), history, _loggerFactory);
        var notifications = new NotificationService(settings);
        _services = new AppServices(settings, theme, history, sessions, notifications);

        settings.Changed += (_, current) =>
        {
            sessions.UpdateOptions(current.ToTransferOptions());
            if (current.Language != Loc.Instance.Language)
            {
                Loc.Instance.SetLanguage(current.Language);
            }

            theme.Apply(current.Theme);
        };

        var window = new MainWindow(_services, new MainViewModel(_services));
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            // Ends the session, stops the local service, cancels transfers and deletes partial files.
            try
            {
                _services.Sessions.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ex)
            {
                AppLog.Error("Shutdown did not complete cleanly", ex.InnerException);
            }

            _services.Notifications.Dispose();
        }

        _loggerFactory?.Dispose();
        AppLog.Info("Fluxio stopped");
        AppLog.Flush();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled UI exception", e.Exception);
        Dialogs.Error("Fluxio", Loc.Instance["Error.Unexpected"] + Environment.NewLine + Environment.NewLine + e.Exception.Message);
        e.Handled = true;
    }
}
