using System.Net;
using System.Text;
using Fluxio.Transfer.Hosting;
using Fluxio.Transfer.Network;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Security;
using Fluxio.Transfer.Storage;
using Fluxio.Transfer.Transfers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fluxio.Transfer.Sessions;

public enum SessionEventKind
{
    SessionStarted,
    DevicePaired,
    DeviceApproved,
    IncomingRequest,
    IncomingAutoAccepted,
    OfferCreated,
    OfferAccepted,
    OfferDeclined,
    FileFinished,
    BatchFinished,
    SessionEnded,
}

public sealed class SessionEventArgs(SessionEventKind kind, TransferSession session, TransferBatch? batch = null, TransferFile? file = null) : EventArgs
{
    public SessionEventKind Kind { get; } = kind;

    public TransferSession Session { get; } = session;

    public TransferBatch? Batch { get; } = batch;

    public TransferFile? File { get; } = file;
}

/// <summary>
/// Owns the single active pairing session and the local HTTP service. The service only
/// listens while a session exists, and only on the selected LAN address.
/// UI callers use the public members; the HTTP endpoints use the internal ones.
/// </summary>
public sealed partial class SessionManager : IAsyncDisposable
{
    /// <summary>A phone that has not been heard from for this long is shown as "not responding".</summary>
    public static readonly TimeSpan ResponsiveThreshold = TimeSpan.FromSeconds(40);

    private const int MaxRecentBatches = 100;
    private const int MaxEndedTokens = 32;

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _gate = new();
    private readonly IHistorySink? _history;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly ITimer _watchdog;
    private readonly List<TransferBatch> _recent = [];
    private readonly Dictionary<string, CloseReason> _endedTokens = new(StringComparer.Ordinal);
    private readonly Queue<string> _endedOrder = new();
    private TransferOptions _options;
    private TransferSession? _current;
    private LocalTransferServer? _server;
    private int _watchdogBusy;
    private bool _disposed;

    public SessionManager(TransferOptions options, IHistorySink? history = null, ILoggerFactory? loggerFactory = null, TimeProvider? timeProvider = null)
    {
        _options = options;
        _history = history;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _log = _loggerFactory.CreateLogger<SessionManager>();
        _time = timeProvider ?? TimeProvider.System;
        _watchdog = _time.CreateTimer(_ => _ = RunWatchdogAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    /// <summary>Raised on a background thread. UI code must marshal to its dispatcher.</summary>
    public event EventHandler<SessionEventArgs>? SessionChanged;

    public TransferOptions Options => Volatile.Read(ref _options);

    /// <summary>The latest session (it stays available after it ended so the UI can show why).</summary>
    public TransferSession? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Batches of this app run, newest last (including ended sessions).</summary>
    public IReadOnlyList<TransferBatch> RecentBatches
    {
        get
        {
            lock (_gate)
            {
                return [.. _recent];
            }
        }
    }

    public DateTimeOffset Now => _time.GetUtcNow();

    public void UpdateOptions(TransferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Volatile.Write(ref _options, options);
    }

    public bool IsDeviceResponsive(TransferSession session) =>
        session.Device is { } device && Now - device.LastSeen < ResponsiveThreshold;

    /// <summary>
    /// Ends the current session (if any) and starts a new one with a fresh id and secret, so a
    /// previously shown QR code stops working immediately.
    /// </summary>
    public async Task<TransferSession> StartSessionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CloseCoreAsync(CloseReason.Replaced, null).ConfigureAwait(false);

            var options = Options;
            var address = options.BindAddressOverride
                ?? NetworkSelector.SelectBest(options.PreferredInterfaceId)?.Address
                ?? throw new FluxioException(FluxioErrorCode.NoNetwork, "No local network connection was found.");

            var server = await LocalTransferServer.StartAsync(address, options.PreferredPort, this, _loggerFactory, cancellationToken)
                .ConfigureAwait(false);

            var now = Now;
            var session = new TransferSession(
                Tokens.NewToken(16), Tokens.NewToken(32), server.BaseUrl, address, server.Port,
                options.ComputerName, now, now + options.PairingWindow)
            {
                Status = SessionStatus.AwaitingScan,
            };

            lock (_gate)
            {
                _server = server;
                _current = session;
            }

            _log.LogInformation("Session started on port {Port}", server.Port);
            Raise(new SessionEventArgs(SessionEventKind.SessionStarted, session));
            return session;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Ends the current session (or only <paramref name="onlyIf"/> when given).</summary>
    public async Task CloseSessionAsync(CloseReason reason = CloseReason.ClosedByUser, TransferSession? onlyIf = null)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseCoreAsync(reason, onlyIf).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public void ApproveDevice()
    {
        var session = Current ?? throw new FluxioException(FluxioErrorCode.InvalidState, "There is no session.");
        lock (session.Sync)
        {
            if (session.Status != SessionStatus.AwaitingApproval || session.Device is null)
            {
                throw new FluxioException(FluxioErrorCode.InvalidState, "No phone is waiting for approval.");
            }

            session.Device.Approved = true;
            session.Status = SessionStatus.Connected;
            session.ConnectedAt = Now;
            session.ApprovalExpiresAt = null;
        }

        session.Touch();
        Raise(new SessionEventArgs(SessionEventKind.DeviceApproved, session));
    }

    /// <summary>Refusing a phone ends the session; a new QR code is needed for another attempt.</summary>
    public Task RejectDeviceAsync() => CloseSessionAsync(CloseReason.DeviceRejected, Current);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _watchdog.DisposeAsync().ConfigureAwait(false);
        await CloseSessionAsync(CloseReason.AppShutdown).ConfigureAwait(false);
    }

    // ---- Pairing and authorization (called by the HTTP endpoints) ----

    internal PairResponse Pair(PairRequest request, IPAddress? remote)
    {
        if (string.IsNullOrEmpty(request.SessionId) || string.IsNullOrEmpty(request.Secret))
        {
            throw new FluxioException(FluxioErrorCode.InvalidLink, "The link is incomplete.");
        }

        var session = Current;
        if (session is null || !Tokens.FixedTimeEquals(session.Id, request.SessionId))
        {
            throw new FluxioException(FluxioErrorCode.SessionNotFound, "This QR code is not valid anymore.");
        }

        string token;
        lock (session.Sync)
        {
            var now = Now;
            switch (session.Status)
            {
                case SessionStatus.AwaitingScan when now >= session.PairingExpiresAt:
                case SessionStatus.Expired:
                    throw new FluxioException(FluxioErrorCode.SessionExpired, "The QR code has expired.") { Reason = WireReason(CloseReason.PairingExpired) };
                case SessionStatus.AwaitingApproval or SessionStatus.Connected:
                    throw new FluxioException(FluxioErrorCode.AlreadyPaired, "Another device already used this QR code.");
                case SessionStatus.AwaitingScan:
                    break;
                default:
                    throw new FluxioException(FluxioErrorCode.SessionClosed, "The session has ended.") { Reason = WireReason(session.CloseReason) };
            }

            if (!Tokens.FixedTimeEquals(session.PairingSecret, request.Secret))
            {
                throw new FluxioException(FluxioErrorCode.Unauthorized, "The link is not valid.");
            }

            token = Tokens.NewToken(32);
            session.Device = new PairedDevice(SanitizeLabel(request.DeviceName), remote, Tokens.Hash(token), Tokens.NewMatchCode(), now);
            session.PairingSecret = Tokens.NewToken(32); // The QR code can never be used again.
            session.Status = SessionStatus.AwaitingApproval;
            session.ApprovalExpiresAt = now + Options.ApprovalWindow;
        }

        session.Touch();
        _log.LogInformation("A phone paired and waits for approval");
        Raise(new SessionEventArgs(SessionEventKind.DevicePaired, session));
        return new PairResponse(token, BuildPhoneState(session));
    }

    internal (TransferSession Session, PairedDevice Device) Authenticate(string? authorization, bool requireApproved = true)
    {
        var token = ParseBearer(authorization)
            ?? throw new FluxioException(FluxioErrorCode.Unauthorized, "Missing access token.");
        var hash = Tokens.Hash(token);

        var session = Current;
        if (session?.Device is { } device && Tokens.FixedTimeEquals(device.TokenHash, hash))
        {
            lock (session.Sync)
            {
                if (!session.IsOpen)
                {
                    var code = session.Status == SessionStatus.Expired ? FluxioErrorCode.SessionExpired : FluxioErrorCode.SessionClosed;
                    throw new FluxioException(code, "The session has ended.") { Reason = WireReason(session.CloseReason) };
                }

                device.Seen(Now);
                if (requireApproved && !device.Approved)
                {
                    throw new FluxioException(FluxioErrorCode.DeviceNotApproved, "Waiting for approval on the computer.");
                }
            }

            return (session, device);
        }

        lock (_gate)
        {
            if (_endedTokens.TryGetValue(hash, out var reason))
            {
                throw new FluxioException(FluxioErrorCode.SessionClosed, "The session has ended.") { Reason = WireReason(reason) };
            }
        }

        throw new FluxioException(FluxioErrorCode.Unauthorized, "The access token is not valid.");
    }

    /// <summary>The phone left (pressed "Disconnect").</summary>
    internal void Leave(TransferSession session) =>
        _ = Task.Run(async () =>
        {
            await Task.Delay(250).ConfigureAwait(false); // let the HTTP response go out first
            await CloseSessionAsync(CloseReason.DeviceLeft, session).ConfigureAwait(false);
        });

    // ---- Internals ----

    private async Task CloseCoreAsync(CloseReason reason, TransferSession? onlyIf)
    {
        TransferSession? session;
        LocalTransferServer? server;
        lock (_gate)
        {
            session = _current;
            if (onlyIf is not null && !ReferenceEquals(onlyIf, session))
            {
                return;
            }

            server = _server;
            _server = null;
        }

        if (session is not null)
        {
            var status = reason switch
            {
                CloseReason.PairingExpired or CloseReason.ApprovalExpired => SessionStatus.Expired,
                CloseReason.NetworkLost => SessionStatus.Failed,
                _ => SessionStatus.Closed,
            };

            var ended = new List<TransferFile>();
            string? tokenHash = null;
            var changed = false;
            lock (session.Sync)
            {
                if (session.IsOpen || session.Status == SessionStatus.Preparing)
                {
                    changed = true;
                    session.Status = status;
                    session.CloseReason = reason;
                    session.Tickets.Clear();
                    tokenHash = session.Device?.TokenHash;
                    foreach (var batch in session.BatchesUnsafe)
                    {
                        foreach (var file in batch.Files)
                        {
                            if (!file.IsFinal)
                            {
                                file.State = file.State == FileTransferState.Verifying ? FileTransferState.Failed : FileTransferState.Cancelled;
                                file.Failure = FileFailure.SessionEnded;
                                file.FinishedAt = Now;
                                ended.Add(file);
                            }
                        }

                        if (!batch.IsFinal)
                        {
                            batch.Status = BatchStatus.Cancelled;
                            batch.FinishedAt = Now;
                        }
                    }
                }
            }

            if (changed)
            {
                if (tokenHash is not null)
                {
                    RememberEndedToken(tokenHash, reason);
                }

                foreach (var file in ended)
                {
                    ReleaseFileResources(file);
                    RecordHistory(file);
                }

                session.Touch();
                _log.LogInformation("Session ended: {Reason}", reason);
                Raise(new SessionEventArgs(SessionEventKind.SessionEnded, session));
            }
        }

        if (server is not null)
        {
            // Give long-polling phones a moment to read the final status before the listener stops.
            await Task.Delay(300).ConfigureAwait(false);
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunWatchdogAsync()
    {
        if (Interlocked.Exchange(ref _watchdogBusy, 1) == 1)
        {
            return;
        }

        try
        {
            var session = Current;
            if (session is null || !session.IsOpen)
            {
                return;
            }

            var now = Now;
            CloseReason? reason = null;
            lock (session.Sync)
            {
                if (session.Status == SessionStatus.AwaitingScan && now >= session.PairingExpiresAt)
                {
                    reason = CloseReason.PairingExpired;
                }
                else if (session.Status == SessionStatus.AwaitingApproval && now >= session.ApprovalExpiresAt)
                {
                    reason = CloseReason.ApprovalExpired;
                }
                else if (session.Status == SessionStatus.Connected
                    && session.Device is { } device
                    && now - device.LastSeen >= Options.IdleTimeout
                    && !session.HasActiveTransfers)
                {
                    reason = CloseReason.IdleTimeout;
                }
            }

            if (reason is null && !NetworkSelector.IsAddressAssigned(session.Address))
            {
                reason = CloseReason.NetworkLost;
            }

            if (reason is { } r)
            {
                await CloseSessionAsync(r, session).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Session watchdog failed");
        }
        finally
        {
            Volatile.Write(ref _watchdogBusy, 0);
        }
    }

    private void RememberEndedToken(string tokenHash, CloseReason reason)
    {
        lock (_gate)
        {
            if (_endedTokens.TryAdd(tokenHash, reason))
            {
                _endedOrder.Enqueue(tokenHash);
                while (_endedOrder.Count > MaxEndedTokens)
                {
                    _endedTokens.Remove(_endedOrder.Dequeue());
                }
            }
        }
    }

    private void AddRecent(TransferBatch batch)
    {
        lock (_gate)
        {
            _recent.Add(batch);
            if (_recent.Count > MaxRecentBatches)
            {
                _recent.RemoveAt(0);
            }
        }
    }

    public void ClearFinishedBatches()
    {
        lock (_gate)
        {
            _recent.RemoveAll(b => b.IsFinal);
        }
    }

    private void Raise(SessionEventArgs args)
    {
        try
        {
            SessionChanged?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "A session event handler failed");
        }
    }

    private static string? ParseBearer(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorization[prefix.Length..].Trim();
        return token.Length is >= 20 and <= 128 ? token : null;
    }

    internal static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "Phone";
        }

        var builder = new StringBuilder();
        foreach (var c in label.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '(' or ')' or '/' or '·' or ',')
            {
                builder.Append(c);
            }

            if (builder.Length >= 48)
            {
                break;
            }
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? "Phone" : result;
    }

    internal static string WireReason(CloseReason reason) => FluxioWire.Camel(reason.ToString());
}
