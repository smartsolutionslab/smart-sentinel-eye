using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.SystemVariables.Api;
using SmartSentinelEye.SystemVariables.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddSystemVariablesInfrastructure();
builder.Services.AddSystemVariablesApi();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapSystemVariableEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
