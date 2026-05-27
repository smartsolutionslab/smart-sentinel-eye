using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.SystemVariables.Api;
using SmartSentinelEye.SystemVariables.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddSystemVariablesInfrastructure();
builder.Services.AddSystemVariablesApi();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

app.MapSystemVariableEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
