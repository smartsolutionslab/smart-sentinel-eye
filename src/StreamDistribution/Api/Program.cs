using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Api;
using SmartSentinelEye.StreamDistribution.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddStreamDistributionInfrastructure();
builder.Services.AddStreamDistributionApi();
builder.Services.AddOpenApi();

// Spec 208 (#2284): /streams/authorize is MediaMTX's anonymous external-auth
// hook and otherwise accepts requests without limit. Limits come from config
// so the production default and the integration lane's small ceiling
// (AppHost.cs, isE2ETests) both apply without a recompile.
int whepAuthorizePermitLimit =
    builder.Configuration.GetValue<int?>("WhepAuthorizeRateLimiting:PermitLimit") ?? 2000;
TimeSpan whepAuthorizeWindow =
    builder.Configuration.GetValue<TimeSpan?>("WhepAuthorizeRateLimiting:Window") ?? TimeSpan.FromMinutes(1);

// Spec 208 review S13: a misconfigured PermitLimit <= 0 or a zero/negative
// Window binds successfully and only throws inside the rate limiter on the
// first real request — turning a config typo into a 500-generating outage
// on MediaMTX's own hook, discovered at runtime under load rather than at
// startup. Fail the host at startup instead (ADR-0105). Window's guard reads
// TotalMilliseconds rather than Ticks because it stays well within int range
// for any realistic window (the production default is 60 000 ms).
Ensure.That(whepAuthorizePermitLimit).AtLeast(1);
int whepAuthorizeWindowMilliseconds = (int)whepAuthorizeWindow.TotalMilliseconds;
Ensure.That(whepAuthorizeWindowMilliseconds).AtLeast(1);

builder.Services.AddMemoryCache();

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
bool IsNewWhepAuthorizeThrottleTransition(IMemoryCache cache, string partition)
{
    if (cache.TryGetValue(partition, out _))
    {
        return false;
    }

    cache.Set(partition, true, whepAuthorizeWindow);
    return true;
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = (context, _) =>
    {
        // Same "ip:{RemoteIpAddress}" spelling as the policy's own partition
        // key below, so a transition log record names exactly the partition
        // the limiter itself keyed on.
        string partition = $"ip:{context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        IMemoryCache cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
        if (IsNewWhepAuthorizeThrottleTransition(cache, partition))
        {
            context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>()
                .WhepAuthorizeThrottled(partition, whepAuthorizePermitLimit);
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
            $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = whepAuthorizePermitLimit,
                Window = whepAuthorizeWindow,
                QueueLimit = 0,
            }));
});

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();

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
