using System.Diagnostics;

namespace SmartSentinelEye.ScenarioSimulator.CameraSim;

internal static class CameraSimSpanRedaction
{
    internal const string Placeholder = "Redacted";

    private const string UrlFullTag = "url.full";
    private const string SchemeSeparator = "://";
    private const string PathsPrefix = "/v3/config/paths/";

    internal static string Redact(string urlFull)
    {
        var authorityStart = urlFull.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return urlFull;
        }

        var pathStart = urlFull.IndexOf('/', authorityStart + SchemeSeparator.Length);
        if (pathStart < 0
            || string.CompareOrdinal(urlFull, pathStart, PathsPrefix, 0, PathsPrefix.Length) != 0)
        {
            return urlFull;
        }

        var verbStart = pathStart + PathsPrefix.Length;
        var verbEnd = urlFull.IndexOf('/', verbStart);
        return verbEnd <= verbStart || verbEnd + 1 >= urlFull.Length
            ? urlFull
            : string.Concat(urlFull.AsSpan(0, verbEnd + 1), Placeholder);
    }

    internal static void Apply(Activity activity)
    {
        if (activity.GetTagItem(UrlFullTag) is string urlFull)
        {
            activity.SetTag(UrlFullTag, Redact(urlFull));
        }
    }
}
