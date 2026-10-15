namespace SmartSentinelEye.Shared.Kernel;

public static class UrlRedaction
{
    public const string Unparseable = "<unparseable url>";

    public static string ToOrigin(string url)
    {
        Ensure.That(url).IsNotNull();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return Unparseable;
        }

        var origin = $"{uri.Scheme}://{uri.Host}";

        return uri.IsDefaultPort ? origin : $"{origin}:{uri.Port}";
    }
}
