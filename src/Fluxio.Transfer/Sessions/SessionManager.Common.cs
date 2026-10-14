using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Storage;
using Fluxio.Transfer.Transfers;
using Microsoft.Extensions.Logging;

namespace Fluxio.Transfer.Sessions;

// Cancellation, completion bookkeeping, history and the phone-facing state snapshot.
public sealed partial class SessionManager
{
    /// <summary>
    /// Cancels one file (computer side). Other files of the batch continue; files that already
    /// finished are kept. A file in its final integrity check cannot be cancelled any more.
    /// </summary>
    public void CancelFile(string batchId, string fileId)
    {
        var session = Current ?? throw new FluxioException(FluxioErrorCode.InvalidState, "There is no session.");
        CancelFile(session, batchId, fileId);
    }

    /// <summary>Cancels every unfinished file of a batch (computer side). Finished files are kept.</summary>
    public void CancelBatch(string batchId)
    {
        var session = Current ?? throw new FluxioException(FluxioErrorCode.InvalidState, "There is no session.");
        CancelBatch(session, batchId);
    }

    internal void CancelFile(TransferSession session, string batchId, string fileId)
    {
        TransferFile file;
        bool batchFinished;
        lock (session.Sync)
        {
            var batch = session.FindBatch(batchId) ?? throw new FluxioException(FluxioErrorCode.NotFound, "Unknown transfer.");
            file = batch.Files.FirstOrDefault(f => f.Id == fileId) ?? throw new FluxioException(FluxioErrorCode.NotFound, "Unknown file.");
            if (file.IsFinal || file.State == FileTransferState.Verifying)
            {
                return;
            }

            file.State = FileTransferState.Cancelled;
            file.FinishedAt = Now;
            batchFinished = UpdateBatchCompletion(batch);
        }

        ReleaseFileResources(file);
        OnFileFinished(file, batchFinished);
    }

    internal void CancelBatch(TransferSession session, string batchId)
    {
        TransferBatch batch;
        var cancelled = new List<TransferFile>();
        lock (session.Sync)
        {
            batch = session.FindBatch(batchId) ?? throw new FluxioException(FluxioErrorCode.NotFound, "Unknown transfer.");
            if (batch.IsFinal)
            {
                return;
            }

            foreach (var file in batch.Files)
            {
                if (!file.IsFinal && file.State != FileTransferState.Verifying)
                {
                    file.State = FileTransferState.Cancelled;
                    file.FinishedAt = Now;
                    cancelled.Add(file);
                }
            }

            if (batch.Files.All(f => f.IsFinal))
            {
                batch.Status = BatchStatus.Cancelled;
                batch.FinishedAt = Now;
            }
        }

        foreach (var file in cancelled)
        {
            ReleaseFileResources(file);
            RecordHistory(file);
        }

        session.Touch();
        Raise(new SessionEventArgs(SessionEventKind.BatchFinished, session, batch));
    }

    /// <summary>Marks a file failed (if it is not final yet) and releases its resources.</summary>
    internal void FailFile(TransferFile file, FileFailure failure)
    {
        var session = file.Batch.Session;
        bool batchFinished;
        lock (session.Sync)
        {
            if (file.IsFinal)
            {
                return;
            }

            file.State = FileTransferState.Failed;
            file.Failure = failure;
            file.FinishedAt = Now;
            batchFinished = UpdateBatchCompletion(file.Batch);
        }

        ReleaseFileResources(file);
        OnFileFinished(file, batchFinished);
    }

    /// <summary>Call under the session lock. True when the batch just reached its final state.</summary>
    private bool UpdateBatchCompletion(TransferBatch batch)
    {
        if (batch.IsFinal || !batch.Files.All(f => f.IsFinal))
        {
            return false;
        }

        batch.Status = batch.Files.All(f => f.State == FileTransferState.Cancelled) ? BatchStatus.Cancelled : BatchStatus.Finished;
        batch.FinishedAt = Now;
        return true;
    }

    private void OnFileFinished(TransferFile file, bool batchFinished)
    {
        RecordHistory(file);
        var session = file.Batch.Session;
        session.Touch();
        Raise(new SessionEventArgs(SessionEventKind.FileFinished, session, file.Batch, file));
        if (batchFinished)
        {
            Raise(new SessionEventArgs(SessionEventKind.BatchFinished, session, file.Batch));
        }
    }

    /// <summary>Stops streams/hashing and removes partial data of a file that will not complete.</summary>
    private static void ReleaseFileResources(TransferFile file)
    {
        switch (file)
        {
            case IncomingFile incoming:
                incoming.Abort.Cancel();
                // If a chunk is being written, the writer deletes the part file when it stops.
                if (incoming.State != FileTransferState.Completed && incoming.WriteLock.Wait(0))
                {
                    try
                    {
                        IncomingStore.TryDelete(incoming.PartPath);
                    }
                    finally
                    {
                        incoming.WriteLock.Release();
                    }
                }

                break;
            case OutgoingFile outgoing:
                if (outgoing.State != FileTransferState.Completed)
                {
                    outgoing.Lifetime.Cancel();
                }

                break;
        }
    }

    private void RecordHistory(TransferFile file)
    {
        if (_history is null)
        {
            return;
        }

        var outcome = file.State switch
        {
            FileTransferState.Completed => HistoryOutcome.Completed,
            FileTransferState.Skipped => HistoryOutcome.Skipped,
            FileTransferState.Cancelled when file.Failure == FileFailure.Declined => HistoryOutcome.Declined,
            FileTransferState.Cancelled => HistoryOutcome.Cancelled,
            _ => HistoryOutcome.Failed,
        };

        var name = file is IncomingFile { SavedName: { } saved } ? saved : file.Name;
        try
        {
            _history.Record(new HistoryEntry(0, file.FinishedAt ?? Now, file.Batch.Direction, name, file.Size, outcome, file.Batch.DeviceLabel, file.Verified));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not write the transfer history");
        }
    }

    internal PhoneState BuildPhoneState(TransferSession session)
    {
        var options = Options;
        lock (session.Sync)
        {
            var offers = new List<BatchDto>();
            var uploads = new List<BatchDto>();
            foreach (var batch in session.BatchesUnsafe)
            {
                (batch.Direction == TransferDirection.Outgoing ? offers : uploads).Add(FluxioWire.ToDto(batch));
            }

            return new PhoneState(
                session.Version,
                FluxioWire.Status(session.Status),
                session.CloseReason == CloseReason.None ? null : WireReason(session.CloseReason),
                session.ComputerName,
                session.Device?.MatchCode ?? string.Empty,
                session.Device?.Approved ?? false,
                new LimitsDto(options.MaxFileSize, options.MaxFilesPerBatch, options.RecommendedChunkSize, options.InPageDownloadLimit),
                offers,
                uploads);
        }
    }
}
