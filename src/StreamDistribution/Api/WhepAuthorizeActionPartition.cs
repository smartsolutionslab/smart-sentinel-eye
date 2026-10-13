using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace SmartSentinelEye.StreamDistribution.Api;

internal static class WhepAuthorizeActionPartition
{
    // ADR-0171 security review (#2862 blocker 2): MediaMTX's own control-API
    // calls and a real WHEP viewer's open both reach this one anonymous route
    // from MediaMTX's own address, so the limiter in Program.cs used to key on source
    // address alone and put them in the same budget — the health sweep's batch
    // call (and this project's own fixture resets, in the integration lane)
    // could crowd out real viewers under load, and in CI it crowded out this
    // policy's own rate-limit tests sharing the test host's one address.
    //
    // The JSON body's `action` field is what tells the two apart, but the body
    // is not available yet when the limiter picks a partition — that happens in
    // RateLimiterMiddleware, ahead of the Minimal API model binding that would
    // otherwise read it. PeekMediaMtxActionAsync buffers and rewinds the body so
    // the real binder downstream still sees it untouched, then stashes a
    // classification the partition key in Program.cs reads back.
    private const string AuthorizeRoutePath = "/streams/authorize";
    private const string MediaMtxActionPartitionItemKey = "WhepAuthorize.ActionPartition";
    internal const string ApiActionPartition = "api";
    private const string WhepActionPartition = "whep";

    internal static async Task PeekMediaMtxActionAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) ||
            !context.Request.Path.Equals(AuthorizeRoutePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        context.Request.EnableBuffering();

        var action = WhepActionPartition;
        try
        {
            using var body = await JsonDocument.ParseAsync(
                context.Request.Body,
                cancellationToken: CancellationToken.None);
            if (body.RootElement.TryGetProperty("action", out var actionElement) &&
                actionElement.ValueKind == JsonValueKind.String &&
                string.Equals(actionElement.GetString(), "api", StringComparison.Ordinal))
            {
                action = ApiActionPartition;
            }
        }
        catch (JsonException)
        {
            // Malformed body: leave it on the viewer-facing partition and let the
            // endpoint's own model binding refuse it the way it always has.
        }

        context.Request.Body.Position = 0;
        context.Items[MediaMtxActionPartitionItemKey] = action;
    }

    // Only the action classification is factored out, not the whole partition
    // key: WhepAuthorizePartitionKeyTests reads Program.cs's own source and
    // requires the "whep-authorize" policy's partition-key argument to spell
    // out "context.Connection.RemoteIpAddress" inline (FR-003's guard against a
    // regression to a fixed/global bucket) — hiding that behind a helper call
    // would pass this file's own tests while defeating that guard.
    internal static string WhepAuthorizeActionClassification(HttpContext context) =>
        context.Items.TryGetValue(MediaMtxActionPartitionItemKey, out var value) && value is string classified
            ? classified
            : WhepActionPartition;

    // Spec 208 US2 (FR-009). One Warning per partition on the transition into
    // the throttled state, never per refusal — the same Interlocked-guarded
    // once-per-transition discipline WhepAuthValidator.cs:142-151 already
    // applies to the realm-unreachable case on this path (ADR-0118: one OTLP
    // sink, kept readable at exactly the moment a flood would otherwise drown
    // it).
    //
    // Spec 208 review S5: an IMemoryCache entry per partition, absolute-expiring
    // after the `window` parameter (the api or whep budget since #2871), gives the same "re-arm once a full window has
    // passed since the last logged transition" semantic a hand-rolled
    // ConcurrentDictionary<string, DateTimeOffset> computed via AddOrUpdate —
    // but with eviction built in. That dictionary never evicted an entry, so on
    // the one endpoint whose entire threat model is "an anonymous attacker with
    // nothing but network reach", keyed on attacker-influenced input (source
    // addresses), it grew for the process lifetime. IMemoryCache is already
    // part of the ASP.NET Core shared framework (no new package, NFR-002).
    private static bool IsNewWhepAuthorizeThrottleTransition(IMemoryCache cache, string partition, TimeSpan window)
    {
        if (cache.TryGetValue(partition, out _))
        {
            return false;
        }

        cache.Set(partition, true, window);
        return true;
    }

    internal static void LogThrottleTransition(
        HttpContext httpContext,
        string partition,
        int permitLimit,
        TimeSpan window)
    {
        var cache = httpContext.RequestServices.GetRequiredService<IMemoryCache>();
        if (IsNewWhepAuthorizeThrottleTransition(cache, partition, window))
        {
            httpContext.RequestServices.GetRequiredService<ILogger<Program>>()
                .WhepAuthorizeThrottled(partition, permitLimit);
        }
    }
}
