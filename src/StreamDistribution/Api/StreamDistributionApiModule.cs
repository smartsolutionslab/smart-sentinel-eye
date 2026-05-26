using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Queries.Handlers;

namespace SmartSentinelEye.StreamDistribution.Api;

/// <summary>
/// Per-context API composition extension (ADR-0051). Application handlers
/// are registered here so the Api project can resolve them via DI. The
/// <see cref="StreamDistribution.Infrastructure.StreamDistributionInfrastructureModule"/>
/// already registers the command handlers behind <c>ICommandHandler&lt;,&gt;</c>;
/// this method exposes their concrete shape to the Minimal-API endpoints.
/// </summary>
public static class StreamDistributionApiModule
{
    public static IServiceCollection AddStreamDistributionApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<GetStreamQueryHandler>();
        services.AddScoped<ListStreamsQueryHandler>();
        services.AddScoped<ProvisionStreamCommandHandler>();
        services.AddScoped<ReportStreamHealthCommandHandler>();
        services.AddScoped<AuthorizeWhepCommandHandler>();

        return services;
    }
}
