namespace SmartSentinelEye.ScenarioSimulator.CameraSim;

/// <summary>
/// Extracts the MediaMTX path component from a simulated camera's RTSP URL,
/// e.g. <c>rtsp://camera-sim:8554/station-4-roughing</c> -> <c>station-4-roughing</c>.
/// </summary>
public static class RtspPath
{
    private const string ConfigRoot = "/v3/config/paths/";
    private const string AddPrefix = ConfigRoot + "add/";
    private const string ReplacePrefix = ConfigRoot + "replace/";

    private static readonly Uri AddBase = new("https://camera-sim.invalid" + AddPrefix);
    private static readonly Uri ReplaceBase = new("https://camera-sim.invalid" + ReplacePrefix);

    public static bool TryExtract(string rtspUrl, out string path)
    {
        path = string.Empty;

        if (!TryReadCandidate(rtspUrl, out var candidate) || candidate.Length == 0)
        {
            return false;
        }

        // Dot segments survive in more shapes than an empty host (an IPv6 host followed by text, mailto:/ldap:),
        // so the only sound test is where the request string the provisioner sends would land, for both verbs it
        // sends (add, then replace): "../add/x" stays under add/ but resolves outside replace/.
        if (!StaysUnder(AddBase, AddPrefix, candidate) || !StaysUnder(ReplaceBase, ReplacePrefix, candidate))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    // The .NET Uri parser throws IndexOutOfRangeException from TryCreate, Host or AbsolutePath for some shapes
    // (a ':' left before a '/' prefix length, e.g. [1:2:3:4:5:6:7:8:/64]); an address it cannot read is dropped.
    private static bool TryReadCandidate(string rtspUrl, out string candidate)
    {
        candidate = string.Empty;
        try
        {
            // An authority-less URI keeps its dot segments, so the request path would leave the /v3/config/paths/ prefix.
            if (string.IsNullOrWhiteSpace(rtspUrl) ||
                !Uri.TryCreate(rtspUrl, UriKind.Absolute, out var uri) ||
                uri.Host.Length == 0)
            {
                return false;
            }

            candidate = uri.AbsolutePath.Trim('/');
            return true;
        }
        catch (IndexOutOfRangeException)
        {
            candidate = string.Empty;
            return false;
        }
    }

    private static bool StaysUnder(Uri configBase, string prefix, string candidate) =>
        Uri.TryCreate(configBase, prefix + candidate, out var resolved) &&
        resolved.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) &&
        resolved.AbsolutePath.Length > prefix.Length;
}
