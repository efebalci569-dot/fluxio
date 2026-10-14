using Fluxio.Transfer.Sessions;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Transfer.Protocol;

/// <summary>Maps engine objects to their wire representation.</summary>
internal static class FluxioWire
{
    public static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    public static string Status(SessionStatus status) => Camel(status.ToString());

    public static string State(FileTransferState state) => Camel(state.ToString());

    public static BatchDto ToDto(TransferBatch batch) => new(
        batch.Id,
        Camel(batch.Status.ToString()),
        batch.TotalBytes,
        batch.TransferredBytes,
        batch.Files.Select(ToDto).ToList());

    public static FileDto ToDto(TransferFile file) => new(
        file.Id,
        file.Name,
        file.Size,
        State(file.State),
        file.State == FileTransferState.Completed ? file.Size : Math.Min(file.Transferred, file.Size),
        file.Failure == FileFailure.None ? null : Camel(file.Failure.ToString()),
        file.Verified,
        (file as IncomingFile)?.SavedName);
}
