using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Api;

/// <summary>
/// Per-context API composition extension (ADR-0051). Application handlers
/// are registered here so the Api project can resolve them via DI.
/// </summary>
public static class CameraCatalogApiModule
{
    public static IServiceCollection AddCameraCatalogApi(this IServiceCollection services)
    {
        Ensure.That(services).IsNotNull();

        services.AddScoped<RegisterCameraCommandHandler>();
        services.AddScoped<RetireCameraCommandHandler>();
        services.AddScoped<ChangeCameraAddressCommandHandler>();
        services.AddScoped<RenameCameraCommandHandler>();
        services.AddScoped<GetCameraQueryHandler>();
        services.AddScoped<ListCamerasQueryHandler>();

        return services;
    }
}
