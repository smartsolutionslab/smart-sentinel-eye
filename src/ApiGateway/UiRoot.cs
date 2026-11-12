using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace SmartSentinelEye.ApiGateway;

/// <summary>A built single-page bundle on disk, served under <paramref name="RequestPath"/> ("" for the origin's root).</summary>
internal sealed record UiRoot(string RequestPath, string Directory, string EntryFile)
{
    public void ServeFiles(WebApplication app) =>
        app.UseStaticFiles(
            new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(Directory),
                RequestPath = RequestPath,
                OnPrepareResponse = Prepare,
            });

    // The entry file names the hashed bundles, so a stale copy strands the browser on yesterday's build.
    private void Prepare(StaticFileResponseContext context)
    {
        var response = context.Context.Response;
        response.Headers.XContentTypeOptions = "nosniff";
        if (context.Context.Request.Path.Value?.Equals($"{RequestPath}/{EntryFile}", StringComparison.OrdinalIgnoreCase) == true)
        {
            response.Headers.CacheControl = "no-cache";
        }
    }

    // No `nonfile` constraint: the framework's default fallback route never reaches a handler for a path whose last segment
    // has a dot, so a deep link such as /cameras/cam.01 would 404 before this code ran.
    public void MapFallback(WebApplication app)
    {
        var entry = Path.Combine(Directory, EntryFile);
        app.MapFallback(
            RequestPath + "/{**path}",
            async context =>
            {
                var request = context.Request;
                var isRead = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
                var claimsPath = IsPageNavigation(request) || !Path.HasExtension(request.Path.Value ?? string.Empty);
                if (!isRead || !claimsPath)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.CacheControl = "no-cache";
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(entry);
            });
    }

    // A missing script requested by a page must stay a 404, never HTML, so only a navigation may claim a dotted path.
    private static bool IsPageNavigation(HttpRequest request) =>
        request.Headers["Sec-Fetch-Dest"].Any(value => value?.Contains("document", StringComparison.OrdinalIgnoreCase) == true);
}
