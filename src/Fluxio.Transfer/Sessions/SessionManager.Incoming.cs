using System.Buffers;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Security;
using Fluxio.Transfer.Storage;
using Fluxio.Transfer.Transfers;
using Microsoft.Extensions.Logging;

namespace Fluxio.Transfer.Sessions;

// Phone → computer transfers.
public sealed partial class SessionManager
{
    private const int MaxPendingIncomingBatches = 3;
    private const int CopyBufferSize = 256 * 1024;

    /// <summary>The computer user accepts files sent from the phone.</summary>
    public void AcceptIncoming(string batchId, ConflictPolicy policy, string? folder = null)
    {
        var session = Current ?? throw new FluxioException(FluxioErrorCode.InvalidState, "There is no session.");
        IncomingBatch batch;
        lock (session.Sync)
        {
            batch = session.FindBatch(batchId) as IncomingBatch
                ?? throw new FluxioException(FluxioErrorCode.NotFound, "Unknown transfer.");
        }

        var target = FolderPolicy.Validate(folder ?? Options.DownloadFolder);
        AcceptIncomingCore(batch, policy, target);
        session.Touch();
    }

    public void DeclineIncoming(string batchId)
    {
        var session = Current ?? throw new FluxioException(FluxioErrorCode.InvalidState, "There is no session.");
        List<TransferFile> declined;
        IncomingBatch batch;
        lock (session.Sync)
        {
            batch = session.FindBatch(batchId) as IncomingBatch
                ?? throw new FluxioException(FluxioErrorCode.NotFound, "Unknown transfer.");
            if (batch.Status != BatchStatus.AwaitingApproval)
            {
                throw new FluxioException(FluxioErrorCode.InvalidState, "The transfer was already answered.");
            }

            batch.Status = BatchStatus.Declined;
            batch.FinishedAt = Now;
            declined = [];
            foreach (var file in batch.Files)
            {
                file.State = FileTransferState.Cancelled;
                file.Failure = FileFailure.Declined;
                file.FinishedAt = Now;
                declined.Add(file);
            }
        }

        foreach (var file in declined)
        {
            RecordHistory(file);
        }

        session.Touch();
        Raise(new SessionEventArgs(SessionEventKind.BatchFinished, session, batch));
    }

    /// <summary>Names in the batch that already exist in <paramref name="folder"/> (for the conflict prompt).</summary>
    public IReadOnlyList<string> FindConflicts(IncomingBatch batch, string folder)
    {
        var conflicts = new List<string>();
        foreach (var file in batch.Files)
        {
            try
            {
                var path = SafePath.CombineInside(folder, file.Name);
                if (File.Exists(path) || Directory.Exists(path))
                {
                    conflicts.Add(file.Name);
                }
            }
            catch (FluxioException)
            {
            }
        }

        return conflicts;
    }

    internal IncomingBatch CreateIncomingBatch(TransferSession session, CreateUploadRequest request)
    {
        var options = Options;
        var specs = request.Files ?? throw new FluxioException(FluxioErrorCode.BadRequest, "No files were listed.");
        if (specs.Count == 0)
        {
            throw new FluxioException(FluxioErrorCode.BadRequest, "No files were listed.");
        }

        if (specs.Count > options.MaxFilesPerBatch)
        {
            throw new FluxioException(FluxioErrorCode.TooManyFiles, $"At most {options.MaxFilesPerBatch} files can be sent at once.");
        }

        var files = new List<IncomingFile>(specs.Count);
        long total = 0;
        foreach (var spec in specs)
        {
            if (spec.Size < 0)
            {
                throw new FluxioException(FluxioErrorCode.BadRequest, "Invalid file size.");
            }

            if (spec.Size > options.MaxFileSize)
            {
                throw new FluxioException(FluxioErrorCode.TooLarge, "A file is larger than the allowed maximum.");
            }

            total = checked(total + spec.Size);
            files.Add(new IncomingFile(Tokens.NewId(), FileNameSanitizer.Sanitize(spec.Name), spec.Size, SanitizeMediaType(spec.Type)));
        }

        // Fast feedback for the phone; checked again on accept and before every chunk.
        var folder = FolderPolicy.Validate(options.DownloadFolder);
        IncomingStore.EnsureSpace(folder, total, options.DiskReserveBytes);

        var batch = new IncomingBatch(Tokens.NewId(), session, files, Now);
        lock (session.Sync)
        {
            if (!session.IsOpen)
            {
                throw new FluxioException(FluxioErrorCode.SessionClosed, "The session has ended.");
            }

            var waiting = session.BatchesUnsafe.Count(b => b.Direction == TransferDirection.Incoming && b.Status == BatchStatus.AwaitingApproval);
            if (waiting >= MaxPendingIncomingBatches)
            {
                throw new FluxioException(FluxioErrorCode.Busy, "Earlier requests are still waiting for an answer.");
            }

            session.AddBatch(batch);
        }

        AddRecent(batch);
        var autoAccept = !options.RequireIncomingApproval;
        if (autoAccept)
        {
            AcceptIncomingCore(batch, options.DefaultConflictPolicy, folder);
        }

        session.Touch();
        Raise(new SessionEventArgs(autoAccept ? SessionEventKind.IncomingAutoAccepted : SessionEventKind.IncomingRequest, session, batch));
        return batch;
    }

    private void AcceptIncomingCore(IncomingBatch batch, ConflictPolicy policy, string target)
    {
        string temp;
        try
        {
            Directory.CreateDirectory(target);
            temp = IncomingStore.EnsureTempFolder(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw IncomingStore.MapWriteError(ex);
        }

        IncomingStore.EnsureSpace(target, batch.TotalBytes, Options.DiskReserveBytes);

        lock (batch.Session.Sync)
        {
            if (!batch.Session.IsOpen)
            {
                throw new FluxioException(FluxioErrorCode.SessionClosed, "The session has ended.");
            }

            if (batch.Status != BatchStatus.AwaitingApproval)
            {
                throw new FluxioException(FluxioErrorCode.InvalidState, "The transfer was already answered.");
            }

            batch.ConflictPolicy = policy;
            batch.TargetFolder = target;
            batch.TempFolder = temp;
            batch.Status = BatchStatus.InProgress;
            foreach (var file in batch.IncomingFiles)
            {
                file.PartPath = IncomingStore.PartPathFor(temp, file.Id);
            }
        }
    }

    internal ChunkResult GetUploadStatus(TransferSession session, string batchId, string fileId)
    {
        lock (session.Sync)
        {
            var (_, file) = FindIncomingFile(session, batchId, fileId);
            return new ChunkResult(file.Transferred, FluxioWire.State(file.State));
        }
    }

    /// <summary>
    /// Appends one chunk at <paramref name="offset"/> (must equal the bytes already stored).
    /// Data is hashed as it is written; an interrupted chunk keeps the bytes that arrived, and
    /// the phone resumes from the offset reported by <see cref="GetUploadStatus"/>.
    /// </summary>
    internal async Task<ChunkResult> ReceiveChunkAsync(TransferSession session, string batchId, string fileId, long offset, long? contentLength, Stream body, CancellationToken requestAborted)
    {
        var options = Options;
        if (contentLength is not { } length)
        {
            throw new FluxioException(FluxioErrorCode.BadRequest, "Content-Length is required.");
        }

        if (length < 0 || length > options.MaxChunkSize)
        {
            throw new FluxioException(FluxioErrorCode.TooLarge, "The chunk is too large.");
        }

        IncomingBatch batch;
        IncomingFile file;
        lock (session.Sync)
        {
            (batch, file) = FindIncomingFile(session, batchId, fileId);
        }

        if (!await file.WriteLock.WaitAsync(0, requestAborted).ConfigureAwait(false))
        {
            throw new FluxioException(FluxioErrorCode.Busy, "This file is already being written.");
        }

        try
        {
            string partPath;
            string target;
            var started = false;
            lock (session.Sync)
            {
                EnsureIncomingWritable(batch, file);
                if (offset != file.Transferred)
                {
                    throw new FluxioException(FluxioErrorCode.OffsetMismatch, "Unexpected offset.") { Received = file.Transferred };
                }

                if (offset + length > file.Size)
                {
                    throw new FluxioException(FluxioErrorCode.TooLarge, "More data than announced.");
                }

                if (file.State == FileTransferState.Pending)
                {
                    file.State = FileTransferState.InProgress;
                    file.StartedAt = Now;
                    started = true;
                }

                partPath = file.PartPath!;
                target = batch.TargetFolder!;
            }

            if (started)
            {
                session.Touch();
            }

            if (length == 0)
            {
                return new ChunkResult(file.Transferred, FluxioWire.State(file.State));
            }

            try
            {
                IncomingStore.EnsureSpace(target, file.Size - offset, options.DiskReserveBytes);
                await WriteChunkAsync(file, partPath, offset, length, body, requestAborted).ConfigureAwait(false);
            }
            catch (FluxioException ex) when (ex.Code is FluxioErrorCode.InsufficientStorage or FluxioErrorCode.WriteFailed or FluxioErrorCode.PermissionDenied)
            {
                FailFile(file, ex.Code switch
                {
                    FluxioErrorCode.InsufficientStorage => FileFailure.InsufficientStorage,
                    FluxioErrorCode.PermissionDenied => FileFailure.PermissionDenied,
                    _ => FileFailure.WriteFailed,
                });
                throw;
            }

            if (file.Abort.IsCancellationRequested || file.State == FileTransferState.Cancelled)
            {
                throw new FluxioException(FluxioErrorCode.Cancelled, "The transfer was cancelled.");
            }

            return new ChunkResult(file.Transferred, FluxioWire.State(file.State));
        }
        finally
        {
            file.WriteLock.Release();
            if (file.IsFinal && file.State != FileTransferState.Completed)
            {
                IncomingStore.TryDelete(file.PartPath);
            }
        }
    }

    private static async Task WriteChunkAsync(IncomingFile file, string partPath, long offset, long length, Stream body, CancellationToken requestAborted)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, file.Abort.Token);
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            FileStream stream;
            try
            {
                stream = new FileStream(partPath, new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    BufferSize = 0,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw IncomingStore.MapWriteError(ex);
            }

            await using (stream.ConfigureAwait(false))
            {
                try
                {
                    // Drop any bytes past the last acknowledged position (e.g. a write that failed half way).
                    if (stream.Length != offset)
                    {
                        stream.SetLength(offset);
                    }

                    stream.Seek(offset, SeekOrigin.Begin);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw IncomingStore.MapWriteError(ex);
                }

                var remaining = length;
                while (remaining > 0)
                {
                    int read;
                    try
                    {
                        read = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), linked.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException)
                    {
                        break; // Connection dropped or cancelled: keep what was written.
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    try
                    {
                        await stream.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        throw IncomingStore.MapWriteError(ex);
                    }

                    // Only bytes that reached the file are hashed and counted.
                    file.Hasher.AppendData(buffer, 0, read);
                    file.AddTransferred(read);
                    file.Meter.Add(read);
                    remaining -= read;
                }

                try
                {
                    await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw IncomingStore.MapWriteError(ex);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// The phone sends the SHA-256 it computed while reading the file. The file is only stored
    /// under its real name when it matches the hash of the bytes this computer received.
    /// </summary>
    internal FileDto CompleteUpload(TransferSession session, string batchId, string fileId, string? sha256)
    {
        if (!Tokens.IsSha256Hex(sha256))
        {
            throw new FluxioException(FluxioErrorCode.BadRequest, "A SHA-256 hex digest is required.");
        }

        IncomingBatch batch;
        IncomingFile file;
        lock (session.Sync)
        {
            (batch, file) = FindIncomingFile(session, batchId, fileId);
        }

        if (!file.WriteLock.Wait(0))
        {
            throw new FluxioException(FluxioErrorCode.Busy, "This file is still being written.");
        }

        try
        {
            lock (session.Sync)
            {
                if (file.State is FileTransferState.Completed or FileTransferState.Skipped)
                {
                    return FluxioWire.ToDto(file); // Idempotent: the response may have been lost.
                }

                EnsureIncomingWritable(batch, file);
                if (file.Transferred != file.Size)
                {
                    throw new FluxioException(FluxioErrorCode.Incomplete, "Not all data has arrived.") { Received = file.Transferred };
                }

                file.State = FileTransferState.Verifying;
            }

            var actual = Convert.ToHexStringLower(file.Hasher.GetHashAndReset());
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("Integrity check failed for an incoming file");
                FailFile(file, FileFailure.IntegrityMismatch);
                throw new FluxioException(FluxioErrorCode.IntegrityFailed, "The received data does not match the original file.");
            }

            string? saved;
            try
            {
                if (file.Size == 0 && !File.Exists(file.PartPath))
                {
                    File.WriteAllBytes(file.PartPath!, []);
                }

                saved = IncomingStore.Finalize(file.PartPath!, batch.TargetFolder!, file.Name, batch.ConflictPolicy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var mapped = IncomingStore.MapWriteError(ex);
                FailFile(file, mapped.Code == FluxioErrorCode.InsufficientStorage ? FileFailure.InsufficientStorage : FileFailure.WriteFailed);
                throw mapped;
            }

            if (saved is not null && Options.MarkIncomingFiles)
            {
                IncomingStore.MarkAsDownloaded(saved);
            }

            bool batchFinished;
            lock (session.Sync)
            {
                if (saved is null)
                {
                    file.State = FileTransferState.Skipped;
                }
                else
                {
                    file.State = FileTransferState.Completed;
                    file.SavedPath = saved;
                    file.SavedName = Path.GetFileName(saved);
                    file.Verified = true;
                }

                file.FinishedAt = Now;
                batchFinished = UpdateBatchCompletion(batch);
            }

            OnFileFinished(file, batchFinished);
            lock (session.Sync)
            {
                return FluxioWire.ToDto(file);
            }
        }
        finally
        {
            file.WriteLock.Release();
            if (file.IsFinal && file.State != FileTransferState.Completed)
            {
                IncomingStore.TryDelete(file.PartPath);
            }
        }
    }

    private static (IncomingBatch Batch, IncomingFile File) FindIncomingFile(TransferSession session, string batchId, string fileId)
    {
        var batch = session.FindBatch(batchId) as IncomingBatch
            ?? throw new FluxioException(FluxioErrorCode.NotFound, "Unknown transfer.");
        var file = batch.IncomingFiles.FirstOrDefault(f => f.Id == fileId)
            ?? throw new FluxioException(FluxioErrorCode.NotFound, "Unknown file.");
        return (batch, file);
    }

    private static void EnsureIncomingWritable(IncomingBatch batch, IncomingFile file)
    {
        switch (batch.Status)
        {
            case BatchStatus.AwaitingApproval:
                throw new FluxioException(FluxioErrorCode.InvalidState, "The computer has not accepted the files yet.");
            case BatchStatus.Declined:
                throw new FluxioException(FluxioErrorCode.Declined, "The computer declined the files.");
            case BatchStatus.Cancelled:
                throw new FluxioException(FluxioErrorCode.Cancelled, "The transfer was cancelled.");
        }

        switch (file.State)
        {
            case FileTransferState.Cancelled:
                throw new FluxioException(FluxioErrorCode.Cancelled, "The transfer was cancelled.");
            case FileTransferState.Failed:
                throw new FluxioException(FluxioErrorCode.InvalidState, "The file transfer failed.");
            case FileTransferState.Completed or FileTransferState.Skipped or FileTransferState.Verifying:
                throw new FluxioException(FluxioErrorCode.InvalidState, "The file is already complete.");
        }
    }

    private static string? SanitizeMediaType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type) || type.Length > 100)
        {
            return null;
        }

        return type.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '+' or '-') ? type : null;
    }
}
