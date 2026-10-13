using SmartSentinelEye.AuditObservability.Api;
using SmartSentinelEye.AuditObservability.Infrastructure;
using SmartSentinelEye.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddAuditObservabilityInfrastructure();
builder.Services.AddAuditObservabilityApi();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuditEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
