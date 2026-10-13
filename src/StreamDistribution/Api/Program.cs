using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Api;
using SmartSentinelEye.StreamDistribution.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddStreamDistributionInfrastructure();
builder.Services.AddStreamDistributionApi();
builder.Services.AddOpenApi();

// Spec 208 (#2284): /streams/authorize is MediaMTX's anonymous external-auth
// hook and otherwise accepts requests without limit. Limits come from config
// so the production default and the integration lane's small ceiling
// (AppHost.cs, isE2ETests) both apply without a recompile.
var whepAuthorizePermitLimit =
    builder.Configuration.GetValue<int?>("WhepAuthorizeRateLimiting:PermitLimit") ?? 2000;
var whepAuthorizeWindow =
    builder.Configuration.GetValue<TimeSpan?>("WhepAuthorizeRateLimiting:Window") ?? TimeSpan.FromMinutes(1);

// #2871: the `api` partition used to share this same ceiling (see the
// classification split below), sized only against the two WHEP test
// classes' own direct call counts (AppHost.cs's accounting comment). It was
// never sized against ITS OWN consumers — the 2-second health sweep
// (StreamHealthWatcher), MediaMtxReconciler's startup pass, and every
// StreamDistribution integration test's own fixture resets/add/list/delete
// calls, all landing in one fixed window keyed by this one test host
// address. Under CI load that total exceeds a 50/10s ceiling; MediaMTX
// reports ANY non-2xx answer from this hook (429 included) to its own
// caller as a flat 401, which is exactly the "intermittent 401" #2871 chased
// — confirmed by directly hammering MediaMTX's control API with 150
// concurrent, correctly-credentialed calls under the old shared ceiling: 46
// got through, 104 came back 401. A separate, far larger ceiling for `api`
// is safe to size generously: unlike a WHEP open (RSA/JWT signature
// verification), a failed `api` attempt costs one cheap SHA-256 compare
// (MediaMtxApiAuthenticator), so a higher ceiling does not reopen the
// CPU-exhaustion finding ADR-0171 closed.
var mediaMtxApiAuthorizePermitLimit =
    builder.Configuration.GetValue<int?>("MediaMtxApiAuthorizeRateLimiting:PermitLimit") ?? 2000;
var mediaMtxApiAuthorizeWindow =
    builder.Configuration.GetValue<TimeSpan?>("MediaMtxApiAuthorizeRateLimiting:Window") ?? TimeSpan.FromMinutes(1);

// Spec 208 review S13: a misconfigured PermitLimit <= 0 or a zero/negative
// Window binds successfully and only throws inside the rate limiter on the
// first real request — turning a config typo into a 500-generating outage
// on MediaMTX's own hook, discovered at runtime under load rather than at
// startup. Fail the host at startup instead (ADR-0105). Window's guard reads
// TotalMilliseconds rather than Ticks because it stays well within int range
// for any realistic window (the production default is 60 000 ms).
Ensure.That(whepAuthorizePermitLimit).AtLeast(1);
var whepAuthorizeWindowMilliseconds = (int)whepAuthorizeWindow.TotalMilliseconds;
Ensure.That(whepAuthorizeWindowMilliseconds).AtLeast(1);
Ensure.That(mediaMtxApiAuthorizePermitLimit).AtLeast(1);
var mediaMtxApiAuthorizeWindowMilliseconds = (int)mediaMtxApiAuthorizeWindow.TotalMilliseconds;
Ensure.That(mediaMtxApiAuthorizeWindowMilliseconds).AtLeast(1);

builder.Services.AddMemoryCache();

// ADR-0171 security review (#2862 blocker 2): MediaMTX's own control-API
// calls and a real WHEP viewer's open both reach this one anonymous route
// from MediaMTX's own address, so the limiter below used to key on source
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
// classification the partition key below reads back.
const string AuthorizeRoutePath = "/streams/authorize";
const string MediaMtxActionPartitionItemKey = "WhepAuthorize.ActionPartition";
const string ApiActionPartition = "api";
const string WhepActionPartition = "whep";

static async Task PeekMediaMtxActionAsync(HttpContext context)
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
        using var body = await JsonDocument.ParseAsync(context.Request.Body);
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
static string WhepAuthorizeActionClassification(HttpContext context) =>
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
// after whepAuthorizeWindow, gives the same "re-arm once a full window has
// passed since the last logged transition" semantic a hand-rolled
// ConcurrentDictionary<string, DateTimeOffset> computed via AddOrUpdate —
// but with eviction built in. That dictionary never evicted an entry, so on
// the one endpoint whose entire threat model is "an anonymous attacker with
// nothing but network reach", keyed on attacker-influenced input (source
// addresses), it grew for the process lifetime. IMemoryCache is already
// part of the ASP.NET Core shared framework (no new package, NFR-002).
bool IsNewWhepAuthorizeThrottleTransition(IMemoryCache cache, string partition, TimeSpan window)
{
    if (cache.TryGetValue(partition, out _))
    {
        return false;
    }

    cache.Set(partition, true, window);
    return true;
}

// #2871: each classification now has its own ceiling (see
// mediaMtxApiAuthorizePermitLimit above), so the throttle-transition log and
// the limiter's own options factory both need to pick the pair that matches
// the request actually being decided, not always the "whep" pair.
(int PermitLimit, TimeSpan Window) RateLimitBudgetFor(string classification) =>
    classification == ApiActionPartition
        ? (mediaMtxApiAuthorizePermitLimit, mediaMtxApiAuthorizeWindow)
        : (whepAuthorizePermitLimit, whepAuthorizeWindow);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = (context, _) =>
    {
        // Same "ip:{RemoteIpAddress}:action:{classification}" spelling as the
        // policy's own partition key below, so a transition log record names
        // exactly the partition the limiter itself keyed on.
        var classification = WhepAuthorizeActionClassification(context.HttpContext);
        var partition =
            $"ip:{context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"
            + $":action:{classification}";
        (var permitLimit, var window) = RateLimitBudgetFor(classification);
        var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
        if (IsNewWhepAuthorizeThrottleTransition(cache, partition, window))
        {
            context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>()
                .WhepAuthorizeThrottled(partition, permitLimit);
        }

        return ValueTask.CompletedTask;
    };

    options.AddPolicy("whep-authorize", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Source IP, not a global bucket: a global bucket would let one
            // anonymous caller exhaust the whole window and deny MediaMTX
            // itself, turning a CPU-exhaustion finding into a trivially
            // triggered fab-wide outage.
            // Not X-Fab either: it is caller-invented on this anonymous,
            // unauthenticated route, so an attacker defeats it by rotating
            // the header.
            // Also split by the api/whep classification PeekMediaMtxActionAsync
            // stashed on the context (ADR-0171 security review, #2862 blocker
            // 2): MediaMTX's own control-API calls and a real WHEP viewer's
            // open both arrive here from MediaMTX's one address, so source IP
            // alone put them in the same budget.
            $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"
                + $":action:{WhepAuthorizeActionClassification(context)}",
            // #2871: api and whep draw from separate budgets
            // (mediaMtxApiAuthorizePermitLimit vs whepAuthorizePermitLimit) —
            // see that variable's own comment for why `api` needs a much
            // larger one. RateLimitBudgetFor is called once per distinct
            // partition key (GetFixedWindowLimiter caches the limiter it
            // builds), so this still runs once per api/whep split, not once
            // per request.
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = RateLimitBudgetFor(WhepAuthorizeActionClassification(context)).PermitLimit,
                Window = RateLimitBudgetFor(WhepAuthorizeActionClassification(context)).Window,
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();

// Reads the request body's `action` field so the rate limiter below (which
// decides its partition before Minimal API model binding ever runs) can
// split MediaMTX's own control-API traffic from real WHEP viewer opens —
// see PeekMediaMtxActionAsync's own comment. Before UseRateLimiter so the
// classification is available when the policy picks a partition; before
// authentication for the same reason the limiter itself runs there (spec
// 208) — nothing here does authentication work either.
app.Use(async (context, next) =>
{
    await PeekMediaMtxActionAsync(context);
    await next(context);
});

// Before authentication so a throttled request does no authentication work
// either (spec 208).
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStreamEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
