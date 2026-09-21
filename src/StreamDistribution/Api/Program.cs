using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SmartSentinelEye.ServiceDefaults;
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

// Spec 208 US2 (FR-009). One Warning per partition on the transition into
// the throttled state, never per refusal — the same Interlocked-guarded
// once-per-transition discipline WhepAuthValidator.cs:142-151 already
// applies to the realm-unreachable case on this path (ADR-0118: one OTLP
// sink, kept readable at exactly the moment a flood would otherwise drown
// it). Re-armed once a full window has passed since the last logged
// transition, so a later, separate throttling episode for the same
// partition is logged again rather than silenced forever.
ConcurrentDictionary<string, DateTimeOffset> whepAuthorizeThrottleTransitions = new(StringComparer.Ordinal);

bool IsNewWhepAuthorizeThrottleTransition(string partition, DateTimeOffset now)
{
    bool isNewTransition = true;
    whepAuthorizeThrottleTransitions.AddOrUpdate(
        partition,
        _ => now,
        (_, lastLoggedAt) =>
        {
            isNewTransition = now - lastLoggedAt >= whepAuthorizeWindow;
            return isNewTransition ? now : lastLoggedAt;
        });

    return isNewTransition;
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
        if (IsNewWhepAuthorizeThrottleTransition(partition, DateTimeOffset.UtcNow))
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
