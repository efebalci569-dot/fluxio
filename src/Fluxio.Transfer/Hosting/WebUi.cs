using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace Fluxio.Transfer.Hosting;

/// <summary>
/// Serves the mobile web UI (src/Fluxio.Web/dist) from resources embedded in this assembly,
/// so the page and the API share one origin and the files cannot be swapped on disk.
/// </summary>
internal static class WebUi
{
    private const string MissingUiPage = """
        <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Fluxio</title><body style="font-family:system-ui;padding:24px">
        <h1>Fluxio web UI is not built</h1>
        <p>This build of Fluxio was compiled without the phone interface. Run <code>npm run build</code> in src/Fluxio.Web and rebuild.</p>
        </body></html>
        """;

    private static readonly Lazy<IFileProvider?> Provider = new(CreateProvider);

    public static bool IsAvailable => Provider.Value?.GetFileInfo("index.html").Exists == true;

    public static void MapFluxioWebUi(this WebApplication app)
    {
        var provider = Provider.Value;
        if (provider is null || !IsAvailable)
        {
            app.MapGet("/", () => Results.Content(MissingUiPage, "text/html; charset=utf-8"));
            return;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = provider,
            OnPrepareResponse = context =>
            {
                // Vite puts content-hashed files under /assets: safe to cache forever.
                context.Context.Response.Headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/assets")
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            },
        });

        app.MapGet("/", async context =>
        {
            var index = provider.GetFileInfo("index.html");
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await using var stream = index.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
    }

    private static IFileProvider? CreateProvider()
    {
        try
        {
            return new ManifestEmbeddedFileProvider(typeof(WebUi).Assembly, "wwwroot");
        }
        catch (InvalidOperationException)
        {
            return null; // No embedded manifest / no wwwroot (built with SkipWebUiBuild=true).
        }
    }
}
