using Microsoft.Extensions.Http.Resilience;

namespace SmartSentinelEye.ServiceDefaults.Resilience;

/// <summary>
/// Gives every upstream authority its own pipeline instance — its own circuit
/// breaker and concurrency limiter — under the one standard resilience handler,
/// so one upstream's outage stays that upstream's outage (#2959).
///
/// <para>
/// The key is <c>scheme://host:port</c>, lower-cased, and nothing else. The
/// SDK's <c>SelectPipelineByAuthority()</c> keys on
/// <c>Uri.GetLeftPart(UriPartial.Authority)</c>, which includes userinfo, and
/// Polly writes the key into the <c>pipeline.instance</c> metric tag and its log
/// lines — so a credentialed URL would publish its password there. Here the
/// credential never reaches the key, and the same host with and without one
/// shares a breaker. Path and query are not part of it either: one upstream is
/// one breaker however many paths it serves.
/// </para>
///
/// <para>
/// The key is taken from the request URI before service discovery rewrites it,
/// so a logical Aspire name is the authority. The set of keys is never evicted:
/// do not put a client whose authority is caller-supplied under these defaults.
/// </para>
/// </summary>
internal static class UpstreamAuthority
{
    internal static string KeyOf(HttpRequestMessage request)
    {
        var uri = request.RequestUri;

        // A request with no absolute URI has no authority to select by; it
        // shares the instance that serves every such request.
        return uri is { IsAbsoluteUri: true }
            ? $"{uri.Scheme}://{uri.Host}:{uri.Port}".ToLowerInvariant()
            : string.Empty;
    }

    internal static IHttpStandardResiliencePipelineBuilder SelectPipelineByUpstreamAuthority(
        this IHttpStandardResiliencePipelineBuilder builder) =>
        builder.SelectPipelineBy(_ => KeyOf);
}
