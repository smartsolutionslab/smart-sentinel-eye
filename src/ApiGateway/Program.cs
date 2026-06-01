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

// ADR-0106: single YARP reverse proxy at the edge. Routes and clusters come
// from configuration; destinations resolve through Aspire service discovery
// (e.g. "http://camera-catalog" -> the live service endpoint). REST only —
// the realtime WebSocket push (ADR-0076) and WebRTC media stay direct, off
// the latency budget (constitution §IV). TLS terminates at the deploy edge
// (k3s Ingress / Helm, ADR-0024/0025) — the Aspire dev model is all-HTTP, so
// there is no dev HTTPS endpoint here. Rate limiting follows in #1004.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.UseCors();
app.MapReverseProxy();

await app.RunAsync();
