using System.Reflection;
using SmartSentinelEye.Shared.Kernel.Primitives;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// Moves an aggregate's optimistic-concurrency version from a test, so an
/// in-memory repository can mirror <c>AggregateVersionInterceptor</c>.
///
/// <para>
/// Without it every Application-layer version is 0, which is also
/// <c>default(int)</c> — so a handler that ignored the loaded aggregate and
/// compared 0 to 0 passed its whole suite. Both spec-012 contexts shipped
/// concurrency gates tested only at that one value, which proves nothing (see
/// #1246, #1248).
/// </para>
///
/// <para>
/// <c>Version</c>'s setter is <c>protected</c> because EF writes it through
/// the change tracker rather than through the property. A fake standing in for
/// EF has to reach it the same way; widening the domain's surface for the
/// benefit of tests would be worse.
/// </para>
///
/// <para>
/// Lives here rather than in a per-context <c>Fakes</c> folder because
/// <see cref="IVersionedAggregate"/> is a Shared.Kernel concept and every
/// context with a concurrency gate needs the same thing. A copy per test
/// project is how the two would drift.
/// </para>
/// </summary>
public static class AggregateVersions
{
    public static void Bump(IVersionedAggregate aggregate)
    {
        Ensure.That(aggregate).IsNotNull();

        SetTo(aggregate, aggregate.Version.Value + 1);
    }

    /// <summary>
    /// Puts an aggregate at <paramref name="version"/>, for tests that need a
    /// value distinguishable from <c>default(int)</c> without driving a save.
    /// </summary>
    public static void SetTo(IVersionedAggregate aggregate, int version)
    {
        Ensure.That(aggregate).IsNotNull();

        // Resolved from the instance, not a hardcoded aggregate type: the
        // property is declared on AggregateRoot<TIdentifier>, which is an open
        // generic and cannot be named here.
        MethodInfo setter =
            aggregate.GetType()
                .GetProperty(nameof(IVersionedAggregate.Version), BindingFlags.Public | BindingFlags.Instance)
                ?.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException(
                $"{aggregate.GetType().Name}.Version has no setter reachable for an in-memory repository.");

        // Boxed as AggregateVersion, not int: reflection does no implicit
        // conversion, so passing the raw int throws "Object of type
        // System.Int32 cannot be converted to AggregateVersion" at call time.
        setter.Invoke(aggregate, [AggregateVersion.From(version)]);
    }
}
