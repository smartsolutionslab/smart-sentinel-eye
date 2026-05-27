using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>
/// Per-context API composition extension (ADR-0051). Exposes the
/// concrete command/query handler classes for direct resolution by
/// Minimal-API endpoints. The
/// <see cref="Infrastructure.LayoutCompositionInfrastructureModule"/>
/// already registers them behind <c>ICommandHandler&lt;,&gt;</c>; this
/// surfaces them by concrete class to keep the endpoint signatures
/// short.
/// </summary>
public static class LayoutCompositionApiModule
{
    public static IServiceCollection AddLayoutCompositionApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<CreateLayoutDraftCommandHandler>();
        services.AddScoped<PublishRevisionCommandHandler>();
        services.AddScoped<ArchiveRevisionCommandHandler>();
        services.AddScoped<BranchDraftRevisionCommandHandler>();
        services.AddScoped<EditDraftRevisionCommandHandler>();
        services.AddScoped<RevertRevisionCommandHandler>();
        services.AddScoped<GetLayoutQueryHandler>();
        services.AddScoped<ListLayoutsQueryHandler>();

        return services;
    }
}
