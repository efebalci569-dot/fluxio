using Fluxio.Transfer.Sessions;
using Fluxio.Transfer.Storage;

namespace Fluxio.Desktop.Services;

/// <summary>Composition root: the long-lived services shared by all view models.</summary>
public sealed class AppServices(SettingsService settings, ThemeService theme, HistoryStore history, SessionManager sessions, NotificationService notifications)
{
    public SettingsService Settings { get; } = settings;

    public ThemeService Theme { get; } = theme;

    public HistoryStore History { get; } = history;

    public SessionManager Sessions { get; } = sessions;

    public NotificationService Notifications { get; } = notifications;

    public const string GitHubUrl = "https://github.com/efebalci569-dot/fluxio";

    public static string Version =>
        typeof(AppServices).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}
