using SmartSentinelEye.OverlayDesigner.Api;
using SmartSentinelEye.OverlayDesigner.Infrastructure;
using SmartSentinelEye.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddOverlayDesignerInfrastructure();
builder.Services.AddOverlayDesignerApi();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

app.MapOverlayEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
