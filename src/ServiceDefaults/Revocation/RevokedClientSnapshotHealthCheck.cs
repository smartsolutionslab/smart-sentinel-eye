using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartSentinelEye.ServiceDefaults.Revocation;

/// <summary>
/// Reports whether the revocation snapshot is fresh enough to trust
/// (spec 270, ADR-0160 §3). <c>Degraded</c>, never <c>Unhealthy</c>
/// (ADR-0154): a stale or missing snapshot means this replica is admitting
/// tokens it should refuse, which is a security-relevant fact worth
/// surfacing — but it is not a reason to take a service that is otherwise
/// serving correctly out of rotation.
/// </summary>
public sealed class RevokedClientSnapshotHealthCheck(RevokedClientRefresher refresher, TimeProvider clock)
    : IHealthCheck
{
    /// <summary>
    /// Past this many missed refresh periods, the snapshot is not merely a
    /// little behind — something is wrong with the refresh loop itself, not
    /// only with reaching Identity for one attempt.
    /// </summary>
    public const int StaleAfterPeriods = 6;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (refresher.LastSuccessAt is not { } lastSuccessAt)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "No revocation snapshot has loaded yet; tokens are admitted without a revocation check."));
        }

        TimeSpan staleAfter = TimeSpan.FromTicks(RevokedClientRefresher.RefreshPeriod.Ticks * StaleAfterPeriods);
        TimeSpan age = clock.GetUtcNow() - lastSuccessAt;
        if (age > staleAfter)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"The revocation snapshot last loaded {age.TotalSeconds:F0}s ago, past the "
                + $"{staleAfter.TotalSeconds:F0}s staleness bound."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("The revocation snapshot is fresh."));
    }
}
