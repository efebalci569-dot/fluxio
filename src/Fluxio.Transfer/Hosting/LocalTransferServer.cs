using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fluxio.Transfer.Hosting;

/// <summary>Address the service listens on; used to validate Host and Origin headers.</summary>
internal sealed class ServerInfo
{
    public IPAddress Address { get; set; } = IPAddress.Loopback;

    public int Port { get; set; }

    public string HostHeader => $"{Address}:{Port}";

    public string Origin => $"http://{HostHeader}";
}

/// <summary>
/// The embedded Kestrel server. It binds to one LAN address only (never 0.0.0.0) and is
/// started per session and stopped when the session ends.
/// </summary>
internal sealed class LocalTransferServer : IAsyncDisposable
{
    private const int PortAttempts = 20;
    private readonly WebApplication _app;
    private int _disposed;

    private LocalTransferServer(WebApplication app, ServerInfo info)
    {
        _app = app;
        Info = info;
    }

    public ServerInfo Info { get; }

    public int Port => Info.Port;

    public string BaseUrl => Info.Origin;

    public static async Task<LocalTransferServer> StartAsync(IPAddress address, int preferredPort, SessionManager manager, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var ports = preferredPort == 0 ? [0] : Enumerable.Range(preferredPort, PortAttempts).Where(p => p <= IPEndPoint.MaxPort).ToArray();
        Exception? lastError = null;

        foreach (var port in ports)
        {
            var info = new ServerInfo { Address = address, Port = port };
            var app = Build(info, manager, loggerFactory);
            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
                if (port == 0)
                {
                    var bound = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
                    info.Port = bound is null ? 0 : new Uri(bound).Port;
                }

                return new LocalTransferServer(app, info);
            }
            catch (Exception ex) when (IsPortProblem(ex))
            {
                lastError = ex;
                await app.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await app.DisposeAsync().ConfigureAwait(false);
                throw new FluxioException(FluxioErrorCode.ServerStartFailed, "The local transfer service could not start.", ex);
            }
        }

        throw new FluxioException(FluxioErrorCode.PortUnavailable, $"No free port between {preferredPort} and {preferredPort + PortAttempts - 1}.", lastError);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _app.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static WebApplication Build(ServerInfo info, SessionManager manager, ILoggerFactory loggerFactory)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(LocalTransferServer).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
            Args = [],
        });

        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(loggerFactory);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = manager.Options.MaxChunkSize + 64 * 1024;
            kestrel.Limits.MaxConcurrentConnections = 64;
            kestrel.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
            kestrel.Limits.MaxRequestLineSize = 8 * 1024;
            kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
            kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(90);
            kestrel.Listen(info.Address, info.Port, listen => listen.Protocols = HttpProtocols.Http1);
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        builder.Services.AddSingleton(manager);
        builder.Services.AddSingleton(info);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ErrorBody(new ErrorInfo("rateLimited", "Too many requests. Wait a moment and try again.")), token).ConfigureAwait(false);
            };

            // Generous for transfers (chunks + long polling) but stops request floods.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 1200, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // Pairing attempts are rare in normal use.
            options.AddPolicy("pair", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        var app = builder.Build();
        app.UseMiddleware<RequestGuardMiddleware>();
        app.UseRateLimiter();
        app.MapFluxioApi();
        app.MapFluxioWebUi();
        return app;
    }

    private static bool IsPortProblem(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is AddressInUseException)
            {
                return true;
            }

            if (current is SocketException socket && socket.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
                return true; // AccessDenied: the port is in a Windows excluded port range (e.g. Hyper-V).
            }
        }

        return false;
    }
}
