using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;

namespace SmartSentinelEye.OverlayDesigner.Api;

/// <summary>
/// Per-context API composition extension (ADR-0051). Exposes the
/// concrete command/query handler classes for direct resolution by
/// Minimal-API endpoints. The infrastructure module already registers
/// them behind <c>ICommandHandler&lt;,&gt;</c>; this surfaces them by
/// concrete class so the endpoint signatures stay short.
/// </summary>
public static class OverlayDesignerApiModule
{
    public static IServiceCollection AddOverlayDesignerApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<CreateOverlayDraftCommandHandler>();
        services.AddScoped<PublishRevisionCommandHandler>();
        services.AddScoped<ArchiveRevisionCommandHandler>();
        services.AddScoped<GetOverlayQueryHandler>();
        services.AddScoped<ListOverlaysQueryHandler>();

        return services;
    }
}
