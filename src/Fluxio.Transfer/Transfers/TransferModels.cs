using System.Security.Cryptography;
using Fluxio.Transfer.Sessions;

namespace Fluxio.Transfer.Transfers;

public enum TransferDirection
{
    /// <summary>Phone → computer.</summary>
    Incoming,

    /// <summary>Computer → phone.</summary>
    Outgoing,
}

public enum BatchStatus
{
    /// <summary>Waiting for the receiving side to accept or decline.</summary>
    AwaitingApproval,
    InProgress,

    /// <summary>Every file reached a final state (see the per-file states for the outcome).</summary>
    Finished,
    Declined,
    Cancelled,
}

public enum FileTransferState
{
    Pending,
    InProgress,

    /// <summary>All bytes moved; waiting for the integrity check.</summary>
    Verifying,
    Completed,
    Failed,
    Cancelled,

    /// <summary>Not saved because a file with the same name existed and the user chose "skip".</summary>
    Skipped,
}

public enum FileFailure
{
    None,
    IntegrityMismatch,
    InsufficientStorage,
    WriteFailed,
    ReadFailed,
    PermissionDenied,
    SourceChanged,
    Interrupted,
    Declined,
    SessionEnded,
}

/// <summary>What to do when an incoming file has the same name as an existing file.</summary>
public enum ConflictPolicy
{
    /// <summary>Keep both: save as "name (1).ext".</summary>
    Rename,
    Overwrite,
    Skip,
}

/// <summary>
/// A file inside a batch. State is mutated under the owning session's lock; the byte
/// counter is updated lock-free by the streaming code.
/// </summary>
public abstract class TransferFile
{
    private long _transferred;

    protected TransferFile(string id, string name, long size)
    {
        Id = id;
        Name = name;
        Size = size;
    }

    public string Id { get; }

    /// <summary>Display name (already sanitized). Never contains a directory part.</summary>
    public string Name { get; }

    public long Size { get; }

    public FileTransferState State { get; internal set; } = FileTransferState.Pending;

    public FileFailure Failure { get; internal set; }

    public long Transferred => Interlocked.Read(ref _transferred);

    public SpeedMeter Meter { get; } = new();

    public DateTimeOffset? StartedAt { get; internal set; }

    public DateTimeOffset? FinishedAt { get; internal set; }

    /// <summary>True when the receiver confirmed the SHA-256 of the content.</summary>
    public bool Verified { get; internal set; }

    public TransferBatch Batch { get; internal set; } = null!;

    public bool IsFinal => State is FileTransferState.Completed or FileTransferState.Failed
        or FileTransferState.Cancelled or FileTransferState.Skipped;

    internal void SetTransferred(long value) => Interlocked.Exchange(ref _transferred, value);

    internal void AddTransferred(long delta) => Interlocked.Add(ref _transferred, delta);
}

public sealed class IncomingFile : TransferFile
{
    internal IncomingFile(string id, string name, long size, string? mediaType)
        : base(id, name, size)
    {
        MediaType = mediaType;
    }

    /// <summary>MIME type reported by the phone. Informational only, never trusted.</summary>
    public string? MediaType { get; }

    /// <summary>Final file name on disk once saved (may differ from <see cref="TransferFile.Name"/> after a rename).</summary>
    public string? SavedName { get; internal set; }

    /// <summary>Full path on this computer once saved. Never sent to the phone.</summary>
    public string? SavedPath { get; internal set; }

    internal string? PartPath { get; set; }

    internal IncrementalHash Hasher { get; } = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    internal SemaphoreSlim WriteLock { get; } = new(1, 1);

    internal CancellationTokenSource Abort { get; } = new();
}

public sealed class OutgoingFile : TransferFile
{
    internal OutgoingFile(string id, string name, long size, string sourcePath, DateTime sourceLastWriteUtc)
        : base(id, name, size)
    {
        SourcePath = sourcePath;
        SourceLastWriteUtc = sourceLastWriteUtc;
    }

    /// <summary>Full path of the file being sent. Never sent to the phone.</summary>
    public string SourcePath { get; }

    internal DateTime SourceLastWriteUtc { get; }

    /// <summary>SHA-256 of the source, computed in the background when the batch is created.</summary>
    internal Task<string> HashTask { get; set; } = Task.FromResult(string.Empty);

    private long _hashedBytes;

    public long HashedBytes => Interlocked.Read(ref _hashedBytes);

    public bool HashReady => HashTask.IsCompletedSuccessfully;

    internal void SetHashed(long value) => Interlocked.Exchange(ref _hashedBytes, value);

    /// <summary>Cancels hashing and any running stream when the file is cancelled.</summary>
    internal CancellationTokenSource Lifetime { get; } = new();

    /// <summary>The stream currently serving this file (a retry replaces it).</summary>
    internal CancellationTokenSource? ActiveStream { get; set; }

    /// <summary>True once a download through the browser's own download manager finished.</summary>
    public bool SentWithoutVerification { get; internal set; }
}

public abstract class TransferBatch
{
    protected TransferBatch(string id, TransferDirection direction, TransferSession session, IReadOnlyList<TransferFile> files, DateTimeOffset createdAt)
    {
        Id = id;
        Direction = direction;
        Session = session;
        Files = files;
        CreatedAt = createdAt;
        DeviceLabel = session.Device?.Label ?? string.Empty;
        foreach (var file in files)
        {
            file.Batch = this;
        }
    }

    public string Id { get; }

    public TransferDirection Direction { get; }

    public TransferSession Session { get; }

    public IReadOnlyList<TransferFile> Files { get; }

    public DateTimeOffset CreatedAt { get; }

    public string DeviceLabel { get; }

    public BatchStatus Status { get; internal set; } = BatchStatus.AwaitingApproval;

    public DateTimeOffset? FinishedAt { get; internal set; }

    public long TotalBytes => Files.Sum(f => f.Size);

    public long TransferredBytes => Files.Sum(f => f.State == FileTransferState.Completed ? f.Size : Math.Min(f.Transferred, f.Size));

    public double BytesPerSecond => Files.Sum(f => f.Meter.BytesPerSecond);

    public bool IsFinal => Status is BatchStatus.Finished or BatchStatus.Declined or BatchStatus.Cancelled;

    public int CountIn(FileTransferState state) => Files.Count(f => f.State == state);
}

public sealed class IncomingBatch : TransferBatch
{
    internal IncomingBatch(string id, TransferSession session, IReadOnlyList<IncomingFile> files, DateTimeOffset createdAt)
        : base(id, TransferDirection.Incoming, session, files, createdAt)
    {
        IncomingFiles = files;
    }

    public IReadOnlyList<IncomingFile> IncomingFiles { get; }

    public ConflictPolicy ConflictPolicy { get; internal set; } = ConflictPolicy.Rename;

    /// <summary>Folder chosen when the batch was accepted.</summary>
    public string? TargetFolder { get; internal set; }

    internal string? TempFolder { get; set; }
}

public sealed class OutgoingBatch : TransferBatch
{
    internal OutgoingBatch(string id, TransferSession session, IReadOnlyList<OutgoingFile> files, DateTimeOffset createdAt)
        : base(id, TransferDirection.Outgoing, session, files, createdAt)
    {
        OutgoingFiles = files;
    }

    public IReadOnlyList<OutgoingFile> OutgoingFiles { get; }
}
