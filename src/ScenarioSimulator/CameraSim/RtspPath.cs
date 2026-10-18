namespace SmartSentinelEye.ScenarioSimulator.CameraSim;

/// <summary>
/// Extracts the MediaMTX path component from a simulated camera's RTSP URL,
/// e.g. <c>rtsp://camera-sim:8554/station-4-roughing</c> -> <c>station-4-roughing</c>.
/// </summary>
public static class RtspPath
{
    private const string ConfigPrefix = "/v3/config/paths/add/";

    private static readonly Uri ConfigBase = new("https://camera-sim.invalid" + ConfigPrefix);

    public static bool TryExtract(string rtspUrl, out string path)
    {
        path = string.Empty;

        // An authority-less URI keeps its dot segments, so the request path would leave the /v3/config/paths/ prefix.
        if (string.IsNullOrWhiteSpace(rtspUrl) ||
            !Uri.TryCreate(rtspUrl, UriKind.Absolute, out var uri) ||
            uri.Host.Length == 0)
        {
            return false;
        }

        var candidate = uri.AbsolutePath.Trim('/');
        if (candidate.Length == 0)
        {
            return false;
        }

        // Dot segments survive in more shapes than an empty host (an IPv6 host followed by text, mailto:/ldap:),
        // so the only sound test is where the request the provisioner builds from this path would land.
        if (!Uri.TryCreate(ConfigBase, candidate, out var resolved) ||
            !resolved.AbsolutePath.StartsWith(ConfigPrefix, StringComparison.Ordinal) ||
            resolved.AbsolutePath.Length == ConfigPrefix.Length)
        {
            return false;
        }

        path = candidate;
        return true;
    }
}
