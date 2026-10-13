using SmartSentinelEye.Identity.Api;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddIdentityInfrastructure();
builder.Services.AddIdentityApi();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDevicesEndpoints();
app.MapKiosksEndpoints();
app.MapWebhookRotationEndpoints();
app.MapRevocationEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
