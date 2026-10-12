using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartSentinelEye.Architecture.Tests.HealthChecks;

/// <summary>
/// Spec 339 (#2571), plan §5. A pure function from a host's composed health
/// check registrations to the names of the ones that violate ADR-0154
/// clause 1: every check other than the framework's own "self" liveness
/// check must declare a failure status other than <see cref="HealthStatus.Unhealthy"/>.
///
/// <para>
/// The exemption is pinned to <c>self</c>'s exact shape (spec §5, FR-003),
/// not its name alone, so a second check cannot borrow it by calling itself
/// "self": the tags must equal exactly <c>{ "live" }</c>, the factory must
/// produce <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.DelegateHealthCheck"/>
/// — internal to the framework, so compared by <see cref="Type.FullName"/> —
/// and there must be exactly one such registration on the host. Zero, or a
/// registration named "self" that fails any part of the shape, is an
/// offender; more than one registration named "self" makes every one of
/// them an offender.
/// </para>
///
/// <para>
/// <b>Known limitation, accepted (backend-reviewer pass on PR #2872):</b>
/// this is shape-matching, not behaviour-matching. A registration that is
/// the only "self", tagged exactly <c>"live"</c>, and backed by a real
/// <c>DelegateHealthCheck</c> is exempt even if the delegate itself probes a
/// dependency and can return <see cref="HealthStatus.Unhealthy"/> — the
/// rule has no way to inspect what a delegate does at runtime.
/// </para>
/// </summary>
internal static class HealthCheckFailureStatusRule
{
    private const string ExemptName = "self";
    private const string ExemptTag = "live";
    private const string ExemptFactoryTypeFullName = "Microsoft.Extensions.Diagnostics.HealthChecks.DelegateHealthCheck";

    internal static IReadOnlyList<string> Offenders(
        IReadOnlyCollection<HealthCheckRegistration> registrations, IServiceProvider services)
    {
        List<string> offenders = [];

        List<HealthCheckRegistration> selfCandidates = [.. registrations.Where(
            registration => string.Equals(registration.Name, ExemptName, StringComparison.Ordinal))];

        if (selfCandidates.Count == 0)
        {
            offenders.Add($"\"{ExemptName}\" missing: no registration named \"{ExemptName}\" was found (ADR-0154 clause 1).");
        }
        else if (selfCandidates.Count > 1)
        {
            foreach (HealthCheckRegistration candidate in selfCandidates)
            {
                offenders.Add(Describe(candidate, services, $"more than one registration named \"{ExemptName}\" ({selfCandidates.Count} found)"));
            }
        }
        else if (!IsExemptShape(selfCandidates[0], services, out string? shapeFailureReason))
        {
            offenders.Add(Describe(selfCandidates[0], services, shapeFailureReason!));
        }

        foreach (HealthCheckRegistration registration in registrations)
        {
            if (string.Equals(registration.Name, ExemptName, StringComparison.Ordinal))
            {
                continue;
            }

            if (registration.FailureStatus == HealthStatus.Unhealthy)
            {
                offenders.Add(Describe(
                    registration, services, "failure status is the framework default Unhealthy (ADR-0154 clause 1)"));
            }
        }

        return offenders;
    }

    private static bool IsExemptShape(
        HealthCheckRegistration registration, IServiceProvider services, out string? failureReason)
    {
        if (!TagsEqualExactly(registration.Tags, ExemptTag))
        {
            failureReason = $"tags are not exactly {{\"{ExemptTag}\"}}";
            return false;
        }

        string? factoryTypeFullName = ResolveFactoryTypeFullName(registration, services);
        if (!string.Equals(factoryTypeFullName, ExemptFactoryTypeFullName, StringComparison.Ordinal))
        {
            failureReason = $"factory type is {factoryTypeFullName ?? "unknown"}, not {ExemptFactoryTypeFullName}";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TagsEqualExactly(IEnumerable<string> tags, string expectedTag)
    {
        List<string> actual = [.. tags];
        return actual.Count == 1 && string.Equals(actual[0], expectedTag, StringComparison.Ordinal);
    }

    private static string? ResolveFactoryTypeFullName(HealthCheckRegistration registration, IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        IHealthCheck check = registration.Factory(scope.ServiceProvider);

        return check.GetType().FullName;
    }

    private static string Describe(HealthCheckRegistration registration, IServiceProvider services, string reason)
    {
        string? type = ResolveFactoryTypeFullName(registration, services);
        string tags = string.Join(", ", registration.Tags);

        return $"\"{registration.Name}\" (failureStatus={registration.FailureStatus}, tags=[{tags}], type={type}): {reason}";
    }
}
