using Microsoft.AspNetCore.Authentication.JwtBearer;
using SmartSentinelEye.LayoutComposition.Api;
using SmartSentinelEye.LayoutComposition.Infrastructure;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;
using SmartSentinelEye.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBearerAuthentication();

// SignalR JS clients can't set the Authorization header on the
// WebSocket upgrade request; they pass the bearer via the
// "?access_token=..." query string per the documented Microsoft pattern.
// This hook copies that value back into context.Token before the
// existing JwtBearer pipeline validates it. Scoped to the hub path so
// REST endpoints stay header-only.
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events ??= new JwtBearerEvents();
    Func<MessageReceivedContext, Task>? existing = options.Events.OnMessageReceived;
    options.Events.OnMessageReceived = async context =>
    {
        if (existing is not null)
        {
            await existing(context).ConfigureAwait(false);
        }
        if (string.IsNullOrEmpty(context.Token) &&
            context.HttpContext.Request.Path.StartsWithSegments(LayoutLifecycleHub.Path) &&
            context.Request.Query.TryGetValue("access_token", out Microsoft.Extensions.Primitives.StringValues accessToken))
        {
            context.Token = accessToken.ToString();
        }
    };
});

builder.AddLayoutCompositionInfrastructure();
builder.Services.AddLayoutCompositionApi();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

app.MapLayoutEndpoints();
app.MapHub<LayoutLifecycleHub>(LayoutLifecycleHub.Path);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();
