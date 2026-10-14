using System.Buffers;
using System.Security.Cryptography;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Security;
using Fluxio.Transfer.Transfers;
using Microsoft.Extensions.Logging;

namespace Fluxio.Transfer.Sessions;

// Computer → phone transfers.
public sealed partial class SessionManager
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Offers files to the connected phone. Each file is hashed (SHA-256) in the background so
    /// the phone can verify what it downloaded. Paths stay on this computer; the phone only
    /// sees names, sizes and random ids.
    /// </summary>
    public OutgoingBatch OfferFiles(IReadOnlyList<string> paths)
    {
        var session = Current ?? throw new FluxioException(FluxioErrorCode.InvalidState, "There is no session.");
        if (paths.Count == 0)
        {
            throw new FluxioException(FluxioErrorCode.BadRequest, "No files were selected.");
        }

        if (paths.Count > Options.MaxFilesPerBatch)
        {
            throw new FluxioException(FluxioErrorCode.TooManyFiles, $"At most {Options.MaxFilesPerBatch} files can be sent at once.");
        }

        var files = new List<OutgoingFile>(paths.Count);
        foreach (var path in paths)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists)
                {
                    throw new FluxioException(FluxioErrorCode.ReadFailed, "A selected file no longer exists.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new FluxioException(FluxioErrorCode.ReadFailed, "A selected file cannot be read.", ex);
            }

            files.Add(new OutgoingFile(Tokens.NewId(), FileNameSanitizer.Sanitize(info.Name), info.Length, info.FullName, info.LastWriteTimeUtc));
        }

        var batch = new OutgoingBatch(Tokens.NewId(), session, files, Now);
        lock (session.Sync)
        {
            if (session.Status != SessionStatus.Connected)
            {
                throw new FluxioException(FluxioErrorCode.InvalidState, "No approved phone is connected.");
            }

            session.AddBatch(batch);
        }

        AddRecent(batch);
        foreach (var file in files)
        {
            file.HashTask = Task.Run(() => HashSourceAsync(file), CancellationToken.None);
            file.HashTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        session.Touch();
        Raise(new SessionEventArgs(SessionEventKind.OfferCreated, session, batch));
        return batch;
    }

    internal void RespondToOffer(TransferSession session, string batchId, bool accept)
    {
        OutgoingBatch batch;
        var declined = new List<TransferFile>();
        lock (session.Sync)
        {
            batch = session.FindBatch(batchId) as OutgoingBatch
                ?? throw new FluxioException(FluxioErrorCode.NotFound, "Unknown transfer.");
            if (batch.Status != BatchStatus.AwaitingApproval)
            {
                throw new FluxioException(FluxioErrorCode.InvalidState, "The offer was already answered.");
            }

            if (accept)
            {
                batch.Status = BatchStatus.InProgress;
            }
            else
            {
                batch.Status = BatchStatus.Declined;
                batch.FinishedAt = Now;
                foreach (var file in batch.Files.Where(f => !f.IsFinal))
                {
                    file.State = FileTransferState.Cancelled;
                    file.Failure = FileFailure.Declined;
                    file.FinishedAt = Now;
                    declined.Add(file);
                }
            }
        }

        foreach (var file in declined)
        {
            ReleaseFileResources(file);
            RecordHistory(file);
        }

        session.Touch();
        Raise(new SessionEventArgs(accept ? SessionEventKind.OfferAccepted : SessionEventKind.OfferDeclined, session, batch));
        if (!accept)
        {
            Raise(new SessionEventArgs(SessionEventKind.BatchFinished, session, batch));
        }
    }

    /// <summary>
    /// Starts serving a file. A newer request for the same file (a retry after a network error)
    /// replaces the older stream. Returns a token that is cancelled when the file is cancelled.
    /// </summary>
    internal (OutgoingFile File, CancellationTokenSource Stream) BeginDownload(TransferSession session, string batchId, string fileId, CancellationToken requestAborted)
    {
        OutgoingFile file;
        lock (session.Sync)
        {
            (_, file) = FindOutgoingFile(session, batchId, fileId);
            EnsureOutgoingReadable(file);
        }

        if (!SourceUnchanged(file))
        {
            FailFile(file, FileFailure.SourceChanged);
            throw new FluxioException(FluxioErrorCode.SourceChanged, "The file changed on the computer after it was offered.");
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, file.Lifetime.Token);
        CancellationTokenSource? previous;
        var started = false;
        lock (session.Sync)
        {
            EnsureOutgoingReadable(file);
            previous = file.ActiveStream;
            file.ActiveStream = cts;
            if (file.State is FileTransferState.Pending or FileTransferState.Verifying)
            {
                started = file.State == FileTransferState.Pending;
                file.State = FileTransferState.InProgress;
                file.StartedAt ??= Now;
            }
        }

        previous?.Cancel();
        if (started)
        {
            session.Touch();
        }

        return (file, cts);
    }

    /// <summary>Called when a download stream ended (fully sent, interrupted or failed).</summary>
    internal void EndDownload(OutgoingFile file, CancellationTokenSource stream, bool allSent, bool viaBrowserDownload, FileFailure failure)
    {
        var session = file.Batch.Session;
        var finished = false;
        var batchFinished = false;
        var changed = false;
        lock (session.Sync)
        {
            if (ReferenceEquals(file.ActiveStream, stream))
            {
                file.ActiveStream = null;
            }

            if (!file.IsFinal && file.State == FileTransferState.InProgress)
            {
                if (failure != FileFailure.None)
                {
                    file.State = FileTransferState.Failed;
                    file.Failure = failure;
                    file.FinishedAt = Now;
                    finished = true;
                }
                else if (allSent && viaBrowserDownload)
                {
                    // The browser's download manager saved it; the page cannot verify the content.
                    file.State = FileTransferState.Completed;
                    file.SentWithoutVerification = true;
                    file.FinishedAt = Now;
                    finished = true;
                }
                else if (allSent)
                {
                    file.State = FileTransferState.Verifying;
                    changed = true;
                }

                if (finished)
                {
                    batchFinished = UpdateBatchCompletion(file.Batch);
                }
            }
        }

        stream.Dispose();
        if (finished)
        {
            OnFileFinished(file, batchFinished);
        }
        else if (changed)
        {
            session.Touch();
        }
    }

    /// <summary>Waits (up to <paramref name="wait"/>) for the SHA-256 of the source file.</summary>
    internal async Task<HashResponse> GetExpectedHashAsync(TransferSession session, string batchId, string fileId, TimeSpan wait, CancellationToken cancellationToken)
    {
        OutgoingFile file;
        lock (session.Sync)
        {
            (_, file) = FindOutgoingFile(session, batchId, fileId);
            if (file.State == FileTransferState.Cancelled)
            {
                throw new FluxioException(FluxioErrorCode.Cancelled, "The transfer was cancelled.");
            }
        }

        try
        {
            var hash = await file.HashTask.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            return new HashResponse(true, hash);
        }
        catch (TimeoutException)
        {
            return new HashResponse(false, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (file.Lifetime.IsCancellationRequested)
            {
                throw new FluxioException(FluxioErrorCode.Cancelled, "The transfer was cancelled.");
            }

            var failure = ex is FluxioException { Code: FluxioErrorCode.SourceChanged } ? FileFailure.SourceChanged : FileFailure.ReadFailed;
            FailFile(file, failure);
            throw new FluxioException(failure == FileFailure.SourceChanged ? FluxioErrorCode.SourceChanged : FluxioErrorCode.ReadFailed, "The file could not be read on the computer.", ex);
        }
    }

    /// <summary>The phone reports the SHA-256 of what it received; the file completes only if it matches.</summary>
    internal async Task<FileDto> ConfirmDownloadAsync(TransferSession session, string batchId, string fileId, string? sha256, CancellationToken cancellationToken)
    {
        if (!Tokens.IsSha256Hex(sha256))
        {
            throw new FluxioException(FluxioErrorCode.BadRequest, "A SHA-256 hex digest is required.");
        }

        var expected = await GetExpectedHashAsync(session, batchId, fileId, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (!expected.Ready)
        {
            throw new FluxioException(FluxioErrorCode.NotReady, "The computer is still preparing the file.");
        }

        OutgoingFile file;
        bool batchFinished;
        bool match;
        lock (session.Sync)
        {
            (_, file) = FindOutgoingFile(session, batchId, fileId);
            if (file.State == FileTransferState.Completed)
            {
                return FluxioWire.ToDto(file);
            }

            if (file.State != FileTransferState.Verifying)
            {
                throw new FluxioException(FluxioErrorCode.InvalidState, "The download is not finished.");
            }

            match = string.Equals(expected.Sha256, sha256, StringComparison.OrdinalIgnoreCase);
            file.State = match ? FileTransferState.Completed : FileTransferState.Failed;
            file.Failure = match ? FileFailure.None : FileFailure.IntegrityMismatch;
            file.Verified = match;
            file.FinishedAt = Now;
            batchFinished = UpdateBatchCompletion(file.Batch);
        }

        OnFileFinished(file, batchFinished);
        if (!match)
        {
            _log.LogWarning("Integrity check failed for an outgoing file");
            throw new FluxioException(FluxioErrorCode.IntegrityFailed, "The downloaded data does not match the original file.");
        }

        lock (session.Sync)
        {
            return FluxioWire.ToDto(file);
        }
    }

    /// <summary>
    /// Creates a single-use, short-lived link so the browser's own download manager can fetch a
    /// large file (it cannot send the Authorization header).
    /// </summary>
    internal string CreateDownloadTicket(TransferSession session, string batchId, string fileId)
    {
        lock (session.Sync)
        {
            var (_, file) = FindOutgoingFile(session, batchId, fileId);
            EnsureOutgoingReadable(file);
            var now = Now;
            foreach (var expired in session.Tickets.Where(t => t.Value.ExpiresAt <= now).Select(t => t.Key).ToList())
            {
                session.Tickets.Remove(expired);
            }

            var ticket = Tokens.NewToken(24);
            session.Tickets[ticket] = new DownloadTicket(batchId, fileId, now + TicketLifetime);
            return "/dl/" + ticket;
        }
    }

    internal (TransferSession Session, string BatchId, string FileId) RedeemTicket(string ticket)
    {
        var session = Current;
        if (session is not null)
        {
            lock (session.Sync)
            {
                if (session.Tickets.Remove(ticket, out var entry)
                    && entry.ExpiresAt > Now
                    && session.IsOpen
                    && session.Device?.Approved == true)
                {
                    session.Device.Seen(Now);
                    return (session, entry.BatchId, entry.FileId);
                }
            }
        }

        throw new FluxioException(FluxioErrorCode.Unauthorized, "This download link is not valid anymore.");
    }

    private static (OutgoingBatch Batch, OutgoingFile File) FindOutgoingFile(TransferSession session, string batchId, string fileId)
    {
        var batch = session.FindBatch(batchId) as OutgoingBatch
            ?? throw new FluxioException(FluxioErrorCode.NotFound, "Unknown transfer.");
        var file = batch.OutgoingFiles.FirstOrDefault(f => f.Id == fileId)
            ?? throw new FluxioException(FluxioErrorCode.NotFound, "Unknown file.");
        return (batch, file);
    }

    private static void EnsureOutgoingReadable(OutgoingFile file)
    {
        switch (file.Batch.Status)
        {
            case BatchStatus.AwaitingApproval:
                throw new FluxioException(FluxioErrorCode.InvalidState, "Accept the files first.");
            case BatchStatus.Declined:
                throw new FluxioException(FluxioErrorCode.Declined, "The files were declined.");
            case BatchStatus.Cancelled:
                throw new FluxioException(FluxioErrorCode.Cancelled, "The transfer was cancelled.");
        }

        switch (file.State)
        {
            case FileTransferState.Cancelled:
                throw new FluxioException(FluxioErrorCode.Cancelled, "The transfer was cancelled.");
            case FileTransferState.Failed:
                throw new FluxioException(file.Failure == FileFailure.SourceChanged ? FluxioErrorCode.SourceChanged : FluxioErrorCode.InvalidState, "The file transfer failed.");
            case FileTransferState.Completed or FileTransferState.Skipped:
                throw new FluxioException(FluxioErrorCode.InvalidState, "The file was already delivered.");
        }
    }

    internal static bool SourceUnchanged(OutgoingFile file)
    {
        try
        {
            var info = new FileInfo(file.SourcePath);
            return info.Exists && info.Length == file.Size && info.LastWriteTimeUtc == file.SourceLastWriteUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<string> HashSourceAsync(OutgoingFile file)
    {
        var token = file.Lifetime.Token;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            await using var stream = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                total += read;
                file.SetHashed(total);
            }

            if (total != file.Size || !SourceUnchanged(file))
            {
                throw new FluxioException(FluxioErrorCode.SourceChanged, "The file changed while it was being prepared.");
            }

            return Convert.ToHexStringLower(sha.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
