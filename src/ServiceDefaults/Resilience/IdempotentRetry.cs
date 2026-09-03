using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Resilience;

/// <summary>
/// Confines the standard handler's retry to methods a retry cannot damage
/// (ADR-0143).
///
/// <para>
/// The handler's own predicate looks only at the outcome — 5xx, 408, a transport
/// exception — and never at the method, so a <c>POST</c> is retried exactly like
/// a <c>GET</c>. A request that reached the server, was processed, and lost its
/// response is indistinguishable from one that never arrived, so that retry can
/// apply an effect twice. It already did: <c>POST /devices/register</c> answered
/// a caller with a conflict for the device its own earlier attempt created
/// (#2039).
/// </para>
///
/// <para>
/// ADR-0142 fixed that endpoint by letting a caller opt into replay with an
/// <c>Idempotency-Key</c>. This is the other half, and it protects the endpoints
/// nobody has got to yet: retrying is the behaviour that needs justifying, not
/// the behaviour that needs disabling.
/// </para>
///
/// <para>
/// <b>Timeouts and the circuit breaker are untouched.</b> Only the retry
/// predicate narrows, so a <c>POST</c> still gets its per-attempt timeout, its
/// total budget, and its share of the breaker — it simply gets one attempt.
/// </para>
/// </summary>
public static class IdempotentRetry
{
    /// <summary>
    /// RFC 9110 §9.2.2. <c>POST</c> and <c>PATCH</c> are the two that are not,
    /// and are the two this excludes — <c>PATCH</c> despite most of ours being
    /// idempotent by construction, because "by construction" is a property of a
    /// particular endpoint and not of the method.
    /// </summary>
    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Put
        || method == HttpMethod.Delete
        || method == HttpMethod.Options
        || method == HttpMethod.Trace;

    /// <summary>
    /// Applied to every client through <c>ConfigureHttpClientDefaults</c>.
    /// </summary>
    public static void RetryIdempotentMethodsOnly(HttpStandardResilienceOptions options)
    {
        Ensure.That(options).IsNotNull();

        options.Retry.ShouldHandle = arguments =>
        {
            // The response carries its request; a transport exception has none,
            // so the context is the fallback the handler populates either way.
            HttpRequestMessage? request =
                arguments.Outcome.Result?.RequestMessage ?? arguments.Context.GetRequestMessage();

            // Unknown method, no retry. Failing closed is the whole point: the
            // damage this prevents is silent, and the cost of being wrong is one
            // extra attempt nobody makes.
            if (request is null || !IsIdempotent(request.Method))
            {
                return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(arguments.Outcome));
        };
    }

    /// <summary>
    /// Puts the library's own predicate back for one client, so its
    /// <c>POST</c>s and <c>PATCH</c>es are retried again.
    ///
    /// <para>
    /// For a client whose non-idempotent calls are idempotent <i>in fact</i> — a
    /// token mint, where a second token simply supersedes the first, or a PATCH
    /// that sets a field to a fixed value. Call it where that is true and say
    /// why at the call site; the default exists because it is usually not.
    /// </para>
    /// </summary>
    public static IHttpClientBuilder RetryEveryMethod(this IHttpClientBuilder builder)
    {
        Ensure.That(builder).IsNotNull();

        builder.Services
            .Configure<HttpStandardResilienceOptions>(
                $"{builder.Name}-standard",
                options => options.Retry.ShouldHandle = arguments =>
                    ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(arguments.Outcome)));

        return builder;
    }
}
