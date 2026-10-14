using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fluxio.Transfer;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Desktop.Services;

public enum ThemeChoice
{
    System,
    Dark,
    Light,
}

/// <summary>User preferences, stored as JSON in %APPDATA%\Fluxio\settings.json.</summary>
public sealed record AppSettings
{
    public string Language { get; init; } = "tr";

    public ThemeChoice Theme { get; init; } = ThemeChoice.Dark;

    public string DownloadFolder { get; init; } = string.Empty;

    /// <summary>How long a QR code stays valid (minutes).</summary>
    public int PairingMinutes { get; init; } = 5;

    public bool RequireIncomingApproval { get; init; } = true;

    public ConflictPolicy DefaultConflictPolicy { get; init; } = ConflictPolicy.Rename;

    public bool Notifications { get; init; } = true;

    public bool MarkIncomingFiles { get; init; } = true;

    public string? PreferredInterfaceId { get; init; }

    public int Port { get; init; } = 47800;

    public static readonly int[] PairingChoices = [2, 5, 10, 15];

    public TransferOptions ToTransferOptions() => new()
    {
        DownloadFolder = DownloadFolder,
        PairingWindow = TimeSpan.FromMinutes(PairingMinutes),
        RequireIncomingApproval = RequireIncomingApproval,
        DefaultConflictPolicy = DefaultConflictPolicy,
        MarkIncomingFiles = MarkIncomingFiles,
        PreferredInterfaceId = PreferredInterfaceId,
        PreferredPort = Port,
    };
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private AppSettings _current;

    public SettingsService()
    {
        _current = Normalize(Load());
    }

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings next;
        lock (_gate)
        {
            next = Normalize(change(_current));
            if (next == _current)
            {
                return;
            }

            _current = next;
            Save(next);
        }

        Changed?.Invoke(this, next);
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged settings file must not prevent the app from starting.
            AppLog.Warn("Settings could not be read; defaults are used.");
        }

        return new AppSettings { Language = DefaultLanguage() };
    }

    private static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.RoamingFolder);
            var temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Settings could not be saved.");
        }
    }

    private static AppSettings Normalize(AppSettings settings) => settings with
    {
        Language = settings.Language is "en" or "tr" ? settings.Language : DefaultLanguage(),
        DownloadFolder = string.IsNullOrWhiteSpace(settings.DownloadFolder) ? AppPaths.DefaultReceiveFolder() : settings.DownloadFolder,
        PairingMinutes = AppSettings.PairingChoices.Contains(settings.PairingMinutes) ? settings.PairingMinutes : 5,
        Port = settings.Port is >= 1024 and <= 65000 ? settings.Port : 47800,
    };

    private static string DefaultLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
}
