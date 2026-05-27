using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.SystemVariables.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddSystemVariablesInfrastructure();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

// SystemVariableEndpoints land in PR D (spec 005 Phase 5). Program.cs
// is currently headless on the HTTP side — endpoints register in the
// next PR. The wiring above ensures persistence + Wolverine outbox +
// in-memory reverse-index + seeder hosted service are ready as soon
// as the API project is exercised.

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
