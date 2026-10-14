namespace Fluxio.Transfer;

/// <summary>
/// Every failure the engine can report. The camelCase form of each name is the
/// wire code used by the HTTP API (see docs/api.md) and the key the UIs use to
/// show a localized, actionable message.
/// </summary>
public enum FluxioErrorCode
{
    BadRequest,
    InvalidLink,
    SessionNotFound,
    SessionExpired,
    SessionClosed,
    AlreadyPaired,
    Unauthorized,
    DeviceNotApproved,
    NotFound,
    InvalidState,
    Declined,
    Cancelled,
    OffsetMismatch,
    Incomplete,
    TooLarge,
    TooManyFiles,
    InsufficientStorage,
    WriteFailed,
    ReadFailed,
    PermissionDenied,
    SourceChanged,
    IntegrityFailed,
    NotReady,
    Busy,
    RateLimited,
    ForbiddenOrigin,
    NoNetwork,
    PortUnavailable,
    ServerStartFailed,
    InvalidFolder,
}

public sealed class FluxioException : Exception
{
    public FluxioException(FluxioErrorCode code, string? message = null, Exception? innerException = null)
        : base(message ?? code.ToString(), innerException)
    {
        Code = code;
    }

    public FluxioErrorCode Code { get; }

    /// <summary>Extra machine-readable data for some codes (e.g. the server offset for OffsetMismatch).</summary>
    public long? Received { get; init; }

    /// <summary>For SessionClosed/SessionExpired: why the session ended (wire form of <see cref="Sessions.CloseReason"/>).</summary>
    public string? Reason { get; init; }
}

public static class FluxioErrors
{
    public static string ToWireCode(FluxioErrorCode code)
    {
        var name = code.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static int ToHttpStatus(FluxioErrorCode code) => code switch
    {
        FluxioErrorCode.BadRequest or FluxioErrorCode.InvalidLink => 400,
        FluxioErrorCode.Unauthorized => 401,
        FluxioErrorCode.DeviceNotApproved or FluxioErrorCode.ForbiddenOrigin or FluxioErrorCode.PermissionDenied => 403,
        FluxioErrorCode.NotFound or FluxioErrorCode.SessionNotFound => 404,
        FluxioErrorCode.InvalidState or FluxioErrorCode.OffsetMismatch or FluxioErrorCode.AlreadyPaired
            or FluxioErrorCode.Busy or FluxioErrorCode.Incomplete or FluxioErrorCode.SourceChanged => 409,
        FluxioErrorCode.SessionExpired or FluxioErrorCode.SessionClosed or FluxioErrorCode.Cancelled
            or FluxioErrorCode.Declined => 410,
        FluxioErrorCode.TooLarge or FluxioErrorCode.TooManyFiles => 413,
        FluxioErrorCode.IntegrityFailed => 422,
        FluxioErrorCode.RateLimited => 429,
        FluxioErrorCode.NotReady => 503,
        FluxioErrorCode.InsufficientStorage => 507,
        _ => 500,
    };
}
