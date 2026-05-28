using SmartSentinelEye.EventIngestion.Api;
using SmartSentinelEye.EventIngestion.Infrastructure;
using SmartSentinelEye.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddEventIngestionInfrastructure();
builder.Services.AddEventIngestionApi();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

app.MapEventsEndpoints();
app.MapWebhookIntegrationsEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
