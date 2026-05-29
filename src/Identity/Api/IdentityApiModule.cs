using Microsoft.Extensions.DependencyInjection;

namespace SmartSentinelEye.Identity.Api;

/// <summary>
/// Per-context API composition extension (ADR-0051). Command
/// handlers are already wired in
/// <c>IdentityInfrastructureModule</c>; this hook is here for
/// symmetry with sibling contexts and for future query-handler
/// registrations.
/// </summary>
public static class IdentityApiModule
{
    public static IServiceCollection AddIdentityApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
