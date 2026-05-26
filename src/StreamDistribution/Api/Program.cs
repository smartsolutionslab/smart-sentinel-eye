using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.StreamDistribution.Api;
using SmartSentinelEye.StreamDistribution.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddStreamDistributionInfrastructure();
builder.Services.AddStreamDistributionApi();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

app.MapStreamEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
