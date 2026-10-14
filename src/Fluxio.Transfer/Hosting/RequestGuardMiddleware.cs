using System.Text.Json;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Fluxio.Transfer.Hosting;

/// <summary>
/// First middleware for every request:
/// <list type="bullet">
/// <item>only local-network clients (private / link-local addresses);</item>
/// <item>the Host header must be exactly our address:port (blocks DNS-rebinding);</item>
/// <item>API calls must be same-origin (Origin / Sec-Fetch-Site), so other web pages cannot drive the API;</item>
/// <item>strict security headers (CSP, no framing, no referrer, no sniffing);</item>
/// <item>engine errors become JSON <see cref="ErrorBody"/> responses without internal details.</item>
/// </list>
/// Authentication itself is a bearer token checked by each endpoint (no cookies, so no CSRF).
/// </summary>
internal sealed class RequestGuardMiddleware(RequestDelegate next, ServerInfo info, ILogger<RequestGuardMiddleware> log)
{
    private const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self'; " +
        "connect-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "POST", "PUT", "DELETE" };

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var headers = response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

        var isApi = request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/dl");
        if (isApi)
        {
            headers.CacheControl = "no-store";
        }

        if (!NetworkRules.IsAllowedRemote(context.Connection.RemoteIpAddress))
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "forbiddenOrigin", "Only devices on the local network can connect.");
            return;
        }

        if (!string.Equals(request.Host.Value, info.HostHeader, StringComparison.OrdinalIgnoreCase))
        {
            await WriteErrorAsync(context, StatusCodes.Status421MisdirectedRequest, "forbiddenOrigin", "Unexpected host.");
            return;
        }

        if (!AllowedMethods.Contains(request.Method))
        {
            await WriteErrorAsync(context, StatusCodes.Status405MethodNotAllowed, "badRequest", "Method not allowed.");
            return;
        }

        if (isApi)
        {
            var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
            if (fetchSite.Length > 0 && fetchSite is not ("same-origin" or "none"))
            {
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "forbiddenOrigin", "Cross-site requests are not allowed.");
                return;
            }

            var origin = request.Headers.Origin.ToString();
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
                && origin.Length > 0 && !string.Equals(origin, info.Origin, StringComparison.OrdinalIgnoreCase))
            {
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "forbiddenOrigin", "Cross-origin requests are not allowed.");
                return;
            }
        }

        try
        {
            await next(context);
        }
        catch (FluxioException ex) when (!response.HasStarted)
        {
            await WriteErrorAsync(context, FluxioErrors.ToHttpStatus(ex.Code), FluxioErrors.ToWireCode(ex.Code), ex.Message, ex.Received, ex.Reason);
        }
        catch (BadHttpRequestException ex) when (!response.HasStarted)
        {
            var tooLarge = ex.StatusCode == StatusCodes.Status413PayloadTooLarge;
            await WriteErrorAsync(context, ex.StatusCode, tooLarge ? "tooLarge" : "badRequest", tooLarge ? "The request is too large." : "Malformed request.");
        }
        catch (JsonException) when (!response.HasStarted)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "badRequest", "Malformed JSON.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing to answer.
        }
        catch (Exception ex) when (!response.HasStarted)
        {
            log.LogError(ex, "Unhandled error in {Method} request", request.Method);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, "serverError", "Something went wrong on the computer.");
        }
    }

    internal static Task WriteErrorAsync(HttpContext context, int status, string code, string message, long? received = null, string? reason = null)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(new ErrorBody(new ErrorInfo(code, message, received, reason)), context.RequestAborted);
    }
}
