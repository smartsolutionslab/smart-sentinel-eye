using Microsoft.AspNetCore.Builder;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

public static class LayoutHubCors
{
    public static WebApplicationBuilder AddLayoutHubCors(this WebApplicationBuilder builder) => builder;

    public static WebApplication UseLayoutHubCors(this WebApplication app) => app;
}
