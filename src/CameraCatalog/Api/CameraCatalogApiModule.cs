using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;

namespace SmartSentinelEye.CameraCatalog.Api;

/// <summary>
/// Per-context API composition extension (ADR-0051). Application handlers
/// are registered here so the Api project can resolve them via DI.
/// </summary>
public static class CameraCatalogApiModule
{
    public static IServiceCollection AddCameraCatalogApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<RegisterCameraCommandHandler>();

        return services;
    }
}
