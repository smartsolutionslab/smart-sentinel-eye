using Microsoft.Extensions.DependencyInjection;

namespace SmartSentinelEye.AuditObservability.Api;

/// <summary>
/// Per-context API composition extension (ADR-0051). Query
/// handlers are already wired in
/// <c>AuditObservabilityInfrastructureModule</c>; this hook is
/// here for symmetry with sibling contexts and for future
/// API-layer registrations.
/// </summary>
public static class AuditObservabilityApiModule
{
    public static IServiceCollection AddAuditObservabilityApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
