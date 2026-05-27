using SmartSentinelEye.OverlayDesigner.Infrastructure;
using SmartSentinelEye.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddOverlayDesignerInfrastructure();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

// OverlayEndpoints land in PR D (spec 004 Phase 2). This Program.cs is
// currently headless on the HTTP side — endpoints register in the next
// PR; the wiring above ensures persistence + Wolverine outbox + domain
// dispatch are ready as soon as the API project is exercised.

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
