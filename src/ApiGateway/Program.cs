using SmartSentinelEye.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ADR-0106: single YARP reverse proxy at the edge. Routes and clusters come
// from configuration; destinations resolve through Aspire service discovery
// (e.g. "http://camera-catalog" -> the live service endpoint). REST only —
// the realtime WebSocket push (ADR-0076) and WebRTC media stay direct, off
// the latency budget (constitution SS IV). CORS, TLS, and rate limiting are
// added in follow-up issues (#1003/#1004); this scaffold just forwards.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapReverseProxy();

await app.RunAsync();
