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
///
/// <para>
/// <b>Nested token mints.</b> Four authorization handlers mint a token through an
/// opted-in <c>RetryEveryMethod()</c> client from inside each attempt of the outer
/// client's pipeline, so in a Keycloak outage one outer call drives several mints
/// (about 9 token POSTs per call, measured on #2959). Which breaker a failure
/// feeds follows the pinned package's transient predicate
/// (Microsoft.Extensions.Http.Resilience 10.10.0): any <c>HttpRequestException</c>,
/// any timeout rejection, 5xx, 408 and 429 are transient. Keycloak's own breaker
/// is fed by every transient mint outcome — 5xx, 408, 429, a connection failure,
/// the mint's own timeout. A mint failure that reaches the outer handler as an
/// <c>HttpRequestException</c> (any non-success status after the mint's retries,
/// or a connection failure) or as the outer attempt's timeout ALSO counts against
/// THIS upstream's breaker and is retried by this call, so a rejected credential
/// costs 4 token POSTs per outer GET, not 1. Only a <c>BrokenCircuitException</c>
/// from an open Keycloak breaker is non-transient to the outer predicate and ends
/// the call. Accepted: the mint is single-flight and the outer attempt timeout
/// cancels it. Identity's admin client shares Keycloak's authority with its own
/// mint, so they share one breaker.
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
