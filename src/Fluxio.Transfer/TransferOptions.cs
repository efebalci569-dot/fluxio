using System.Net;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Transfer;

/// <summary>
/// Engine configuration. The desktop app builds a new instance from its settings and
/// calls <see cref="Sessions.SessionManager.UpdateOptions"/> whenever they change.
/// Limits are deliberately explicit so they can be documented (docs/security.md).
/// </summary>
public sealed record TransferOptions
{
    public const long GiB = 1024L * 1024 * 1024;
    public const long MiB = 1024L * 1024;

    /// <summary>Folder where accepted incoming files are stored.</summary>
    public string DownloadFolder { get; init; } = DefaultDownloadFolder();

    /// <summary>How long a QR code can be used to pair a phone.</summary>
    public TimeSpan PairingWindow { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the computer user has to approve a phone after it scanned the code.</summary>
    public TimeSpan ApprovalWindow { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>A connected session with no traffic and no running transfer closes after this.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>When false, files sent from the phone are accepted without a prompt (device approval is always required).</summary>
    public bool RequireIncomingApproval { get; init; } = true;

    /// <summary>Policy used when incoming files are auto-accepted.</summary>
    public ConflictPolicy DefaultConflictPolicy { get; init; } = ConflictPolicy.Rename;

    /// <summary>Add the Windows "downloaded from another computer" mark to received files.</summary>
    public bool MarkIncomingFiles { get; init; } = true;

    /// <summary>First port tried; the next 19 ports are tried if it is busy. 0 = any free port.</summary>
    public int PreferredPort { get; init; } = 47800;

    /// <summary>Network interface id (NetworkInterface.Id) chosen by the user, or null for automatic.</summary>
    public string? PreferredInterfaceId { get; init; }

    public long MaxFileSize { get; init; } = 100 * GiB;
    public int MaxFilesPerBatch { get; init; } = 500;
    public int MaxChunkSize { get; init; } = (int)(16 * MiB);
    public int RecommendedChunkSize { get; init; } = (int)(8 * MiB);

    /// <summary>Files up to this size are downloaded by the phone page itself (verified with SHA-256).</summary>
    public long InPageDownloadLimit { get; init; } = 1 * GiB;

    /// <summary>Free space that must remain on the target drive after an incoming transfer.</summary>
    public long DiskReserveBytes { get; init; } = 200 * MiB;

    public string ComputerName { get; init; } = Environment.MachineName;

    /// <summary>Test hook: bind to this address instead of auto-selecting a LAN interface.</summary>
    internal IPAddress? BindAddressOverride { get; init; }

    public static string DefaultDownloadFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Fluxio");
}
