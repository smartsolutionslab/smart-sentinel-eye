namespace SmartSentinelEye.ScenarioSimulator.CameraSim;

/// <summary>
/// Extracts the MediaMTX path component from a simulated camera's RTSP URL,
/// e.g. <c>rtsp://camera-sim:8554/station-4-roughing</c> -> <c>station-4-roughing</c>.
/// </summary>
public static class RtspPath
{
    public static bool TryExtract(string rtspUrl, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(rtspUrl) ||
            !Uri.TryCreate(rtspUrl, UriKind.Absolute, out Uri uri))
        {
            return false;
        }

        string candidate = uri.AbsolutePath.Trim('/');
        if (candidate.Length == 0)
        {
            return false;
        }

        path = candidate;
        return true;
    }
}
