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
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
