using SmartSentinelEye.Identity.Api;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddIdentityInfrastructure();
builder.Services.AddIdentityApi();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDevicesEndpoints();
app.MapKiosksEndpoints();
app.MapWebhookRotationEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
