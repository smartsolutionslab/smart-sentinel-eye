using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers;
using SmartSentinelEye.Shared.Kernel;

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
        Ensure.That(services).IsNotNull();

        services
            .AddScoped<CreateOverlayDraftCommandHandler>()
            .AddScoped<PublishRevisionCommandHandler>()
            .AddScoped<ArchiveRevisionCommandHandler>()
            .AddScoped<BranchDraftRevisionCommandHandler>()
            .AddScoped<EditDraftRevisionCommandHandler>()
            .AddScoped<RevertRevisionCommandHandler>()
            .AddScoped<GetOverlayQueryHandler>()
            .AddScoped<ListOverlaysQueryHandler>();

        return services;
    }
}
