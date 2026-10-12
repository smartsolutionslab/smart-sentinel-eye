using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartSentinelEye.Architecture.Tests.HealthChecks;

/// <summary>
/// Spec 339 (#2571), plan §5. A pure function from a host's composed health
/// check registrations to the names of the ones that violate ADR-0154
/// clause 1: every check other than the framework's own "self" liveness
/// check must declare a failure status other than <see cref="HealthStatus.Unhealthy"/>.
///
/// <para>
/// <b>T003 stub.</b> This currently returns no offenders at all, regardless
/// of input. T004's red test is written against this stub, deliberately: it
/// proves the self-test has bite before the real rule exists (phase 4a,
/// spec §7). T005 replaces this body; the method signature does not change.
/// </para>
/// </summary>
internal static class HealthCheckFailureStatusRule
{
    internal static IReadOnlyList<string> Offenders(
        IReadOnlyCollection<HealthCheckRegistration> registrations, IServiceProvider services) => [];
}
