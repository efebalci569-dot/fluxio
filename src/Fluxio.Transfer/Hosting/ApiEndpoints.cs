using System.Buffers;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Sessions;
using Fluxio.Transfer.Transfers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace Fluxio.Transfer.Hosting;

/// <summary>
/// HTTP API used by the phone page (docs/api.md). Endpoints are thin: authorization and all
/// state changes happen in <see cref="SessionManager"/>.
/// </summary>
internal static class ApiEndpoints
{
    private const int MaxJsonBytes = 256 * 1024;
    private static readonly TimeSpan MaxLongPoll = TimeSpan.FromSeconds(25);

    public static void MapFluxioApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/ping", () => Results.Json(new { app = "fluxio", api = 1 }));
        api.MapPost("/pair", PairAsync).RequireRateLimiting("pair");
        api.MapGet("/state", GetStateAsync);
        api.MapPost("/leave", Leave);

        api.MapPost("/uploads", CreateUploadAsync);
        api.MapGet("/uploads/{batchId}/files/{fileId}", GetUploadStatus);
        api.MapPut("/uploads/{batchId}/files/{fileId}", PutChunkAsync);
        api.MapPost("/uploads/{batchId}/files/{fileId}/complete", CompleteUploadAsync);
        api.MapDelete("/uploads/{batchId}/files/{fileId}", (HttpContext c, SessionManager m, string batchId, string fileId) => CancelFile(c, m, batchId, fileId, TransferDirection.Incoming));
        api.MapDelete("/uploads/{batchId}", (HttpContext c, SessionManager m, string batchId) => CancelBatch(c, m, batchId, TransferDirection.Incoming));

        api.MapPost("/offers/{batchId}/accept", (HttpContext c, SessionManager m, string batchId) => RespondToOffer(c, m, batchId, true));
        api.MapPost("/offers/{batchId}/decline", (HttpContext c, SessionManager m, string batchId) => RespondToOffer(c, m, batchId, false));
        api.MapDelete("/offers/{batchId}", (HttpContext c, SessionManager m, string batchId) => CancelBatch(c, m, batchId, TransferDirection.Outgoing));
        api.MapDelete("/offers/{batchId}/files/{fileId}", (HttpContext c, SessionManager m, string batchId, string fileId) => CancelFile(c, m, batchId, fileId, TransferDirection.Outgoing));
        api.MapGet("/offers/{batchId}/files/{fileId}/content", DownloadAsync);
        api.MapGet("/offers/{batchId}/files/{fileId}/sha256", GetHashAsync);
        api.MapPost("/offers/{batchId}/files/{fileId}/confirm", ConfirmAsync);
        api.MapPost("/offers/{batchId}/files/{fileId}/ticket", CreateTicket);

        app.MapGet("/dl/{ticket}", TicketDownloadAsync);
    }

    private static async Task<IResult> PairAsync(HttpContext context, SessionManager manager)
    {
        var request = await ReadJsonAsync<PairRequest>(context);
        return Results.Ok(manager.Pair(request, context.Connection.RemoteIpAddress));
    }

    private static async Task<IResult> GetStateAsync(HttpContext context, SessionManager manager, long? since, int? wait)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization, requireApproved: false);
        if (since is { } known && wait is > 0)
        {
            var timeout = TimeSpan.FromSeconds(Math.Min(wait.Value, MaxLongPoll.TotalSeconds));
            await session.WaitForChangeAsync(known, timeout, context.RequestAborted);
        }

        return Results.Ok(manager.BuildPhoneState(session));
    }

    private static IResult Leave(HttpContext context, SessionManager manager)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization, requireApproved: false);
        manager.Leave(session);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateUploadAsync(HttpContext context, SessionManager manager)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        var request = await ReadJsonAsync<CreateUploadRequest>(context);
        var batch = manager.CreateIncomingBatch(session, request);
        return Results.Ok(manager.BuildPhoneState(session).Uploads.First(b => b.Id == batch.Id));
    }

    private static IResult GetUploadStatus(HttpContext context, SessionManager manager, string batchId, string fileId)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        return Results.Ok(manager.GetUploadStatus(session, batchId, fileId));
    }

    private static async Task<IResult> PutChunkAsync(HttpContext context, SessionManager manager, string batchId, string fileId, long offset)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = manager.Options.MaxChunkSize;
        }

        var result = await manager.ReceiveChunkAsync(session, batchId, fileId, offset, context.Request.ContentLength, context.Request.Body, context.RequestAborted);
        return Results.Ok(result);
    }

    private static async Task<IResult> CompleteUploadAsync(HttpContext context, SessionManager manager, string batchId, string fileId)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        var request = await ReadJsonAsync<HashRequest>(context);
        return Results.Ok(manager.CompleteUpload(session, batchId, fileId, request.Sha256));
    }

    private static IResult CancelFile(HttpContext context, SessionManager manager, string batchId, string fileId, TransferDirection direction)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        EnsureDirection(session, batchId, direction);
        manager.CancelFile(session, batchId, fileId);
        return Results.NoContent();
    }

    private static IResult CancelBatch(HttpContext context, SessionManager manager, string batchId, TransferDirection direction)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        EnsureDirection(session, batchId, direction);
        manager.CancelBatch(session, batchId);
        return Results.NoContent();
    }

    private static IResult RespondToOffer(HttpContext context, SessionManager manager, string batchId, bool accept)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        manager.RespondToOffer(session, batchId, accept);
        return Results.Ok(manager.BuildPhoneState(session).Offers.First(b => b.Id == batchId));
    }

    private static async Task<IResult> GetHashAsync(HttpContext context, SessionManager manager, string batchId, string fileId, int? wait)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(wait ?? 10, 0, 25));
        return Results.Ok(await manager.GetExpectedHashAsync(session, batchId, fileId, timeout, context.RequestAborted));
    }

    private static async Task<IResult> ConfirmAsync(HttpContext context, SessionManager manager, string batchId, string fileId)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        var request = await ReadJsonAsync<HashRequest>(context);
        return Results.Ok(await manager.ConfirmDownloadAsync(session, batchId, fileId, request.Sha256, context.RequestAborted));
    }

    private static IResult CreateTicket(HttpContext context, SessionManager manager, string batchId, string fileId)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        return Results.Ok(new TicketResponse(manager.CreateDownloadTicket(session, batchId, fileId)));
    }

    private static Task DownloadAsync(HttpContext context, SessionManager manager, string batchId, string fileId)
    {
        var (session, _) = manager.Authenticate(context.Request.Headers.Authorization);
        return StreamFileAsync(context, manager, session, batchId, fileId, viaBrowserDownload: false);
    }

    private static Task TicketDownloadAsync(HttpContext context, SessionManager manager, string ticket)
    {
        var (session, batchId, fileId) = manager.RedeemTicket(ticket);
        return StreamFileAsync(context, manager, session, batchId, fileId, viaBrowserDownload: true);
    }

    /// <summary>Streams a file from disk (never buffered in memory). Supports one "bytes=N-[M]" range for resuming.</summary>
    private static async Task StreamFileAsync(HttpContext context, SessionManager manager, TransferSession session, string batchId, string fileId, bool viaBrowserDownload)
    {
        var (file, stream) = manager.BeginDownload(session, batchId, fileId, context.RequestAborted);
        var allSent = false;
        var failure = FileFailure.None;
        try
        {
            var (start, end, partial) = ParseRange(context.Request, file.Size);

            FileStream source;
            try
            {
                source = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = FileFailure.ReadFailed;
                throw new FluxioException(FluxioErrorCode.ReadFailed, "The file could not be opened on the computer.", ex);
            }

            await using (source.ConfigureAwait(false))
            {
                var response = context.Response;
                var length = file.Size == 0 ? 0 : end - start + 1;
                response.StatusCode = partial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
                response.ContentType = "application/octet-stream";
                response.ContentLength = length;
                response.Headers.AcceptRanges = "bytes";
                if (partial)
                {
                    response.Headers.ContentRange = new ContentRangeHeaderValue(start, end, file.Size).ToString();
                }

                var disposition = new ContentDispositionHeaderValue("attachment");
                disposition.SetHttpFileName(file.Name);
                response.Headers.ContentDisposition = disposition.ToString();

                if (length == 0)
                {
                    allSent = true;
                    return;
                }

                source.Seek(start, SeekOrigin.Begin);
                file.SetTransferred(start);
                var buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
                try
                {
                    var remaining = length;
                    var position = start;
                    while (remaining > 0)
                    {
                        int read;
                        try
                        {
                            read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), stream.Token);
                        }
                        catch (IOException)
                        {
                            failure = FileFailure.ReadFailed;
                            context.Abort();
                            return;
                        }

                        if (read == 0)
                        {
                            failure = FileFailure.SourceChanged; // The file got shorter while sending.
                            context.Abort();
                            return;
                        }

                        await response.Body.WriteAsync(buffer.AsMemory(0, read), stream.Token);
                        position += read;
                        remaining -= read;
                        file.SetTransferred(position);
                        file.Meter.Add(read);
                    }

                    await response.Body.FlushAsync(stream.Token);
                    allSent = end == file.Size - 1;
                    if (allSent && !SessionManager.SourceUnchanged(file))
                    {
                        allSent = false;
                        failure = FileFailure.SourceChanged;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Phone disconnected, a retry replaced this stream, or the transfer was cancelled.
            // Ending without all bytes makes the client see the response as incomplete.
            context.Abort();
        }
        catch (IOException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        finally
        {
            manager.EndDownload(file, stream, allSent, viaBrowserDownload, failure);
        }
    }

    private static (long Start, long End, bool Partial) ParseRange(HttpRequest request, long size)
    {
        var header = request.Headers.Range.ToString();
        if (header.Length == 0 || size == 0)
        {
            return (0, Math.Max(size - 1, 0), false);
        }

        if (!RangeHeaderValue.TryParse(header, out var range)
            || !string.Equals(range.Unit.Value, "bytes", StringComparison.OrdinalIgnoreCase)
            || range.Ranges.Count != 1
            || range.Ranges.First().From is not { } from
            || from >= size)
        {
            throw new FluxioException(FluxioErrorCode.BadRequest, "Unsupported range.");
        }

        var to = range.Ranges.First().To is { } t ? Math.Min(t, size - 1) : size - 1;
        if (to < from)
        {
            throw new FluxioException(FluxioErrorCode.BadRequest, "Unsupported range.");
        }

        return (from, to, true);
    }

    private static void EnsureDirection(TransferSession session, string batchId, TransferDirection direction)
    {
        var batch = session.Batches.FirstOrDefault(b => b.Id == batchId);
        if (batch is null || batch.Direction != direction)
        {
            throw new FluxioException(FluxioErrorCode.NotFound, "Unknown transfer.");
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
        where T : class
    {
        var request = context.Request;
        if (request.ContentLength is > MaxJsonBytes)
        {
            throw new FluxioException(FluxioErrorCode.TooLarge, "The request is too large.");
        }

        if (!request.HasJsonContentType())
        {
            throw new FluxioException(FluxioErrorCode.BadRequest, "JSON is expected.");
        }

        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = MaxJsonBytes;
        }

        return await request.ReadFromJsonAsync<T>(context.RequestAborted)
            ?? throw new FluxioException(FluxioErrorCode.BadRequest, "A JSON body is required.");
    }
}
