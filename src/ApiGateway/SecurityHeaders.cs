namespace SmartSentinelEye.ApiGateway;

internal static class SecurityHeaders
{
    // Headers by content type, not route: HTML reaches the browser as a static file, a fallback or a proxied dev-server page.
    public static void UseGatewaySecurityHeaders(this WebApplication app)
    {
        if (UiHosting.ReadMode(app.Configuration) is not { } mode)
        {
            return;
        }

        var policy = ContentSecurityPolicy.For(mode, ContentSecurityPolicyOrigins.Read(app.Configuration));

        app.Use(
            (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    var response = context.Response;
                    if (response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        response.Headers.ContentSecurityPolicy = policy;
                        response.Headers.XContentTypeOptions = "nosniff";
                        response.Headers.XFrameOptions = "SAMEORIGIN";
                        response.Headers["Referrer-Policy"] = "same-origin";
                        response.Headers["Permissions-Policy"] =
                            "camera=(), microphone=(), geolocation=(), display-capture=(), payment=(), usb=()";
                    }

                    return Task.CompletedTask;
                });

                return next(context);
            });
    }
}
