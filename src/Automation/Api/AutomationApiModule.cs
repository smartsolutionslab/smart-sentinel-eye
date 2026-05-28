using Microsoft.Extensions.DependencyInjection;

namespace SmartSentinelEye.Automation.Api;

/// <summary>
/// Per-context API composition extension (ADR-0051). Handler
/// concrete types are already registered in the Infrastructure
/// module behind <c>ICommandHandler&lt;,&gt;</c>; this hook is here
/// for symmetry with sibling contexts and for future read-side
/// query handler registrations (PR F).
/// </summary>
public static class AutomationApiModule
{
    public static IServiceCollection AddAutomationApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
