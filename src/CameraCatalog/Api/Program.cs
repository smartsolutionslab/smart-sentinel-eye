using SmartSentinelEye.CameraCatalog.Api;
using SmartSentinelEye.CameraCatalog.Infrastructure;
using SmartSentinelEye.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddCameraCatalogInfrastructure();
builder.Services.AddCameraCatalogApi();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapCameraCatalogEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
