namespace SmartSentinelEye.ApiGateway;

internal enum UiMode
{
    Proxy,
    Static,
}

internal sealed record ContentSecurityPolicyOrigins(string IdentityProvider, string LayoutHub, string Media)
{
    internal const string KeyPrefix = "Ui:ContentSecurityPolicy:";

    public static ContentSecurityPolicyOrigins Read(IConfiguration configuration)
    {
        return new(
            Origin(configuration, "IdentityProvider"),
            Origin(configuration, "LayoutHub"),
            Origin(configuration, "Media"));
    }

    private static string Origin(IConfiguration configuration, string name)
    {
        var key = KeyPrefix + name;
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException($"{key} must be set when Ui:Mode is set.");
        }

        if (raw.IndexOfAny([';', '\'', '"', ',']) >= 0 || raw.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException($"{key} must be a single origin; '{raw}' carries policy syntax or whitespace.");
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{key} must be an absolute http or https URL; got '{raw}'.");
        }

        if (uri.UserInfo.Length > 0 || !uri.Host.All(char.IsAscii))
        {
            throw new InvalidOperationException($"{key} must be a bare origin with an ASCII host; '{raw}' carries userinfo or a non-ASCII host.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}

internal static class ContentSecurityPolicy
{
    public static string For(UiMode mode, ContentSecurityPolicyOrigins origins)
    {
        var scriptSource = mode == UiMode.Proxy ? "'self' 'unsafe-inline' 'unsafe-eval'" : "'self'";
        var (identityProvider, layoutHub, media) = origins;

        return $"default-src 'self'; script-src {scriptSource}; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; "
            + $"connect-src 'self' {identityProvider} {layoutHub} {WebSocketTwin(layoutHub)} {media}; "
            + "object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'";
    }

    private static string WebSocketTwin(string origin) =>
        origin.StartsWith("https://", StringComparison.Ordinal)
            ? "wss://" + origin["https://".Length..]
            : "ws://" + origin["http://".Length..];
}
