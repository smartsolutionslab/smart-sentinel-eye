using SmartSentinelEye.Automation.Api;
using SmartSentinelEye.Automation.Infrastructure;
using SmartSentinelEye.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();
builder.AddAutomationInfrastructure();
builder.Services.AddAutomationApi();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapRulesEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
