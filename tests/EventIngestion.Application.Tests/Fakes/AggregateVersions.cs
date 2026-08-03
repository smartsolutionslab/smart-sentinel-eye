using System.Reflection;
using SmartSentinelEye.Shared.Kernel.Primitives;
using WebhookIntegrationAggregate = SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Mirrors <c>AggregateVersionInterceptor</c> for the in-memory repository.
///
/// <para>
/// Without it every Application-layer version is 0, which is also
/// <c>default(int)</c> — so a handler that ignored the loaded aggregate and
/// compared 0 to 0 passed the whole unit suite. That is how the concurrency
/// gate reached `develop` tested only at the one value that proves nothing.
/// </para>
///
/// <para>
/// <c>Version</c>'s setter is <c>protected</c> because EF writes it through
/// the change tracker rather than through the property. A fake standing in
/// for EF has to reach it the same way; widening the domain's surface for the
/// benefit of tests would be worse.
/// </para>
/// </summary>
internal static class AggregateVersions
{
    private static readonly MethodInfo Setter =
        typeof(WebhookIntegrationAggregate)
            .GetProperty(nameof(IVersionedAggregate.Version), BindingFlags.Public | BindingFlags.Instance)
            ?.GetSetMethod(nonPublic: true)
        ?? throw new InvalidOperationException(
            "WebhookIntegration.Version has no setter reachable for the in-memory repository.");

    internal static void Bump(IVersionedAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        Setter.Invoke(aggregate, [aggregate.Version + 1]);
    }

    /// <summary>
    /// Puts an aggregate at <paramref name="version"/>, for tests that need a
    /// value distinguishable from <c>default(int)</c> without driving a save.
    /// </summary>
    internal static void SetTo(IVersionedAggregate aggregate, int version)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        Setter.Invoke(aggregate, [version]);
    }
}
