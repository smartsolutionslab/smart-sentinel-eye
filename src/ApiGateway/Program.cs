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

// ADR-0106 (#1004): per-fab rate limiting at the edge. Requests are partitioned
// by the fab header so one fab's burst cannot starve another, and requests with
// no fab fall back to a per-source-IP partition so unattributed traffic stays
// bounded. Limits come from config under RateLimiting and are tuned per fab at
// deploy time. The gateway does not validate JWTs, which ADR-0106 keeps per
// service, so the partition key is a trusted-edge header rather than a verified
// claim. The policy is named and attached per route below, so the gateway's own
// health endpoints and the k8s liveness and readiness probes are never throttled.
string fabHeader = builder.Configuration["RateLimiting:FabHeader"] ?? "X-Fab";
int permitLimit = builder.Configuration.GetValue<int?>("RateLimiting:PermitLimit") ?? 100;
TimeSpan window = builder.Configuration.GetValue<TimeSpan?>("RateLimiting:Window") ?? TimeSpan.FromMinutes(1);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("per-fab", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveFabPartition(context, fabHeader),
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
// the realtime WebSocket push (ADR-0076) and WebRTC media stay direct, off
// the latency budget (constitution §IV). TLS terminates at the deploy edge
// (k3s Ingress / Helm, ADR-0024/0025) — the Aspire dev model is all-HTTP, so
// there is no dev HTTPS endpoint here. Each route opts into CORS and the
// "per-fab" rate-limit policy via configuration.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.UseCors();
app.UseRateLimiter();
app.MapReverseProxy();

await app.RunAsync();

// Rate-limit partition key: the caller's fab (trusted edge header), or its source
// IP when no fab is presented, so unattributed traffic is still bounded.
static string ResolveFabPartition(HttpContext context, string fabHeader)
{
    string fab = context.Request.Headers[fabHeader].ToString();
    if (!string.IsNullOrWhiteSpace(fab))
    {
        return $"fab:{fab}";
    }

    string remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return $"ip:{remoteIp}";
}
