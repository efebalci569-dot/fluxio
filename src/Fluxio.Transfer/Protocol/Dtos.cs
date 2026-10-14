namespace Fluxio.Transfer.Protocol;

// Wire format of the local HTTP API (JSON, camelCase, enums as camelCase strings).
// Documented in docs/api.md and mirrored in src/Fluxio.Web/src/types/api.ts.

public sealed record PairRequest(string? SessionId, string? Secret, string? DeviceName);

public sealed record PairResponse(string Token, PhoneState State);

public sealed record PhoneState(
    long Version,
    string Status,
    string? CloseReason,
    string ComputerName,
    string MatchCode,
    bool Approved,
    LimitsDto Limits,
    IReadOnlyList<BatchDto> Offers,
    IReadOnlyList<BatchDto> Uploads);

public sealed record LimitsDto(long MaxFileSize, int MaxFilesPerBatch, int ChunkSize, long InPageDownloadLimit);

public sealed record BatchDto(
    string Id,
    string Status,
    long TotalBytes,
    long TransferredBytes,
    IReadOnlyList<FileDto> Files);

public sealed record FileDto(
    string Id,
    string Name,
    long Size,
    string State,
    long Transferred,
    string? Failure,
    bool Verified,
    string? SavedName);

public sealed record UploadFileSpec(string? Name, long Size, string? Type);

public sealed record CreateUploadRequest(IReadOnlyList<UploadFileSpec>? Files);

public sealed record ChunkResult(long Received, string State);

public sealed record HashRequest(string? Sha256);

public sealed record HashResponse(bool Ready, string? Sha256);

public sealed record TicketResponse(string Url);

public sealed record ErrorBody(ErrorInfo Error);

public sealed record ErrorInfo(string Code, string Message, long? Received = null, string? Reason = null);
