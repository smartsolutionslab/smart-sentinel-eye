using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

namespace SmartSentinelEye.SystemVariables.Api;

/// <summary>
/// Per-context API composition extension (ADR-0051). Exposes the
/// concrete command/query handler classes for direct resolution by
/// Minimal-API endpoints. The Infrastructure module already registers
/// them behind <c>ICommandHandler&lt;,&gt;</c>; this surfaces them by
/// concrete class so the endpoint signatures stay short.
/// </summary>
public static class SystemVariablesApiModule
{
    public static IServiceCollection AddSystemVariablesApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<DefineVariableCommandHandler>();
        services.AddScoped<SetVariableValueCommandHandler>();
        services.AddScoped<ArchiveVariableCommandHandler>();
        services.AddScoped<GetVariableQueryHandler>();
        services.AddScoped<ListVariablesQueryHandler>();
        services.AddScoped<GetOverlaySnapshotQueryHandler>();

        return services;
    }
}
