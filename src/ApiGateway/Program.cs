using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SmartSentinelEye.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ADR-0106 (#1003): one CORS policy for the browser apps, defined once at the
// edge instead of per service. Allowed origins come from config
// (Cors:AllowedOrigins) so each environment sets its own; every proxy route
// opts in via "CorsPolicy": "gateway".
string[] corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("gateway", policy =>
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

// ADR-0106 (#1004), as corrected by spec 311 (#2283): per-source rate
// limiting at the edge. Requests are partitioned by the caller's source IP,
// so unattributed traffic stays bounded regardless of what the request
// claims about itself. Limits come from config under RateLimiting. The
// gateway does not validate JWTs, which ADR-0106 keeps per service, and
// X-Fab is a header the caller chose with no credential behind it — using it
// as a partition key let a caller buy a fresh window per value just by
// rotating it, so the partition is the connection, never the request. The
// policy is named and attached per route below, so the gateway's own health
// endpoints and the k8s liveness and readiness probes are never throttled.
int permitLimit = builder.Configuration.GetValue<int?>("RateLimiting:PermitLimit") ?? 100;
TimeSpan window = builder.Configuration.GetValue<TimeSpan?>("RateLimiting:Window") ?? TimeSpan.FromMinutes(1);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("per-source", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveSourcePartition(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
            }));
});

// ADR-0106: single YARP reverse proxy at the edge. Routes and clusters come
// from configuration; destinations resolve through Aspire service discovery
// (e.g. "http://camera-catalog" -> the live service endpoint). REST only —
// the realtime SignalR push (ADR-0152) and WebRTC media stay direct, off
// the latency budget (constitution §IV). TLS terminates at the deploy edge
// (k3s Ingress / Helm, ADR-0024/0025) — the Aspire dev model is all-HTTP, so
// there is no dev HTTPS endpoint here. Each route opts into CORS and the
// "per-source" rate-limit policy via configuration.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.UseCors();
app.UseRateLimiter();

// Spec 208 (#2284) review, BLOCKER 1. /streams/authorize is MediaMTX's own
// anonymous external-auth hook, called directly at the service by service DNS
// via mediamtx.yml, never through this gateway — confirmed by grepping apps/
// for "authorize": apps/shared/src/api/streams.api.ts only calls
// streams/{id}, streams/ and streams/kiosk-latency. The "stream-distribution"
// catch-all below would otherwise proxy this anonymous route straight to the
// AllowAnonymous mapping, from the externally-reachable gateway, with no
// credential and no gateway-level auth. Worse, in this dev/test topology
// every caller reaching stream-distribution collapses into the same
// RemoteIpAddress through Aspire's DCP proxy — MediaMTX's own hook calls and
// any gateway-forwarded traffic alike (see spec 208 spec.md's Assumptions
// section and WhepAuthorizePartitionKeyTests.cs's own remarks) — so an
// anonymous off-box caller reaching this gateway could exhaust the
// whep-authorize partition MediaMTX's legitimate calls share, refusing
// MediaMTX's own calls too and turning the CPU-exhaustion fix into a
// fab-wide video DoS lever. The gateway's own "per-source" limiter does not
// help here since every caller reaching this gateway in this topology
// collapses into the same RemoteIpAddress. This literal route has higher
// routing precedence
// than the catch-all's {**catch-all} segment regardless of registration
// order, because ASP.NET Core routing always prefers a literal segment over
// a catch-all, so this is not order-dependent on the MapReverseProxy call
// below. The other stream-distribution endpoints stay proxied — only this
// one path is carved out.
app.MapPost("/stream-distribution/streams/authorize", () => Results.NotFound());

app.MapReverseProxy();

await app.RunAsync();

// Rate-limit partition key: the caller's source IP. A function of the
// connection, never of the request — nothing the caller sends can select a
// different partition for itself (#2283).
static string ResolveSourcePartition(HttpContext context)
{
    string remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return $"ip:{remoteIp}";
}
