using System.Net;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Transfer.Sessions;

public enum SessionStatus
{
    Preparing,

    /// <summary>The QR code is shown and waiting for a phone.</summary>
    AwaitingScan,

    /// <summary>A phone paired; the computer user must approve it.</summary>
    AwaitingApproval,
    Connected,
    Closed,
    Expired,
    Failed,
}

public enum CloseReason
{
    None,
    ClosedByUser,
    Replaced,
    PairingExpired,
    ApprovalExpired,
    IdleTimeout,
    DeviceRejected,
    DeviceLeft,
    NetworkLost,
    AppShutdown,
}

public sealed class PairedDevice
{
    private long _lastSeenTicks;

    internal PairedDevice(string label, IPAddress? address, string tokenHash, string matchCode, DateTimeOffset pairedAt)
    {
        Label = label;
        Address = address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;
        TokenHash = tokenHash;
        MatchCode = matchCode;
        PairedAt = pairedAt;
        _lastSeenTicks = pairedAt.UtcTicks;
    }

    /// <summary>Description sent by the phone, e.g. "Android · Chrome". Sanitized, not authenticated.</summary>
    public string Label { get; }

    public IPAddress? Address { get; }

    internal string TokenHash { get; }

    /// <summary>Shown on both screens so the user approves the phone they are holding.</summary>
    public string MatchCode { get; }

    public DateTimeOffset PairedAt { get; }

    public bool Approved { get; internal set; }

    public DateTimeOffset LastSeen => new(Interlocked.Read(ref _lastSeenTicks), TimeSpan.Zero);

    internal void Seen(DateTimeOffset now) => Interlocked.Exchange(ref _lastSeenTicks, now.UtcTicks);
}

/// <summary>
/// One pairing between this computer and one phone. Created by <see cref="SessionManager"/>;
/// all mutable state is changed under <see cref="Sync"/>.
/// </summary>
public sealed class TransferSession
{
    private readonly List<TransferBatch> _batches = [];
    private TaskCompletionSource _changed = NewSignal();
    private long _version = 1;

    internal TransferSession(string id, string pairingSecret, string baseUrl, IPAddress address, int port, string computerName, DateTimeOffset createdAt, DateTimeOffset pairingExpiresAt)
    {
        Id = id;
        PairingSecret = pairingSecret;
        BaseUrl = baseUrl;
        Address = address;
        Port = port;
        ComputerName = computerName;
        CreatedAt = createdAt;
        PairingExpiresAt = pairingExpiresAt;
    }

    internal object Sync { get; } = new();

    public string Id { get; }

    internal string PairingSecret { get; set; }

    /// <summary>e.g. http://192.168.1.20:47800</summary>
    public string BaseUrl { get; }

    public IPAddress Address { get; }

    public int Port { get; }

    public string ComputerName { get; }

    /// <summary>
    /// The URL encoded in the QR code. The session id and secret are in the fragment, which
    /// browsers never send to a server or put in a Referer header.
    /// </summary>
    public string PairingUrl => $"{BaseUrl}/#s={Id}&k={PairingSecret}";

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset PairingExpiresAt { get; internal set; }

    /// <summary>Deadline for the computer user to approve a paired phone.</summary>
    public DateTimeOffset? ApprovalExpiresAt { get; internal set; }

    public SessionStatus Status { get; internal set; } = SessionStatus.Preparing;

    public CloseReason CloseReason { get; internal set; }

    public PairedDevice? Device { get; internal set; }

    public DateTimeOffset? ConnectedAt { get; internal set; }

    public bool IsOpen => Status is SessionStatus.AwaitingScan or SessionStatus.AwaitingApproval or SessionStatus.Connected;

    public long Version => Interlocked.Read(ref _version);

    internal Dictionary<string, DownloadTicket> Tickets { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<TransferBatch> Batches
    {
        get
        {
            lock (Sync)
            {
                return [.. _batches];
            }
        }
    }

    public bool HasActiveTransfers
    {
        get
        {
            lock (Sync)
            {
                return _batches.Any(b => b.Status == BatchStatus.InProgress);
            }
        }
    }

    internal void AddBatch(TransferBatch batch) => _batches.Add(batch);

    internal TransferBatch? FindBatch(string id) => _batches.FirstOrDefault(b => b.Id == id);

    /// <summary>Live list; callers must hold <see cref="Sync"/>.</summary>
    internal List<TransferBatch> BatchesUnsafe => _batches;

    /// <summary>Signals long-polling clients that something visible to the phone changed.</summary>
    internal void Touch()
    {
        Interlocked.Increment(ref _version);
        var previous = Interlocked.Exchange(ref _changed, NewSignal());
        previous.TrySetResult();
    }

    /// <summary>Completes when <see cref="Version"/> differs from <paramref name="knownVersion"/> or the timeout passes.</summary>
    public async Task WaitForChangeAsync(long knownVersion, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var signal = Volatile.Read(ref _changed);
        if (Version != knownVersion || timeout <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await signal.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record DownloadTicket(string BatchId, string FileId, DateTimeOffset ExpiresAt);
