using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// The fabs that exist, according to whatever registry of plants the system
/// maintains (spec 019 FR-001).
///
/// <para>
/// Declared here and implemented nowhere in this context. The registry is
/// Keycloak's group tree, which belongs to Identity — and no bounded context
/// may reference another (constitution §III; <c>AllowedCrossContext</c> in
/// <c>BoundaryTests</c> is empty and must stay empty). The implementation
/// lives in <c>MigrationRunner</c>, which is not a bounded context but the
/// composition root for migrations, and already references all nine.
/// </para>
///
/// <para>
/// Returning <see cref="FabIdentifier"/> rather than <c>string</c> is not
/// decoration: the value ends up interpolated into DDL, because Postgres
/// cannot parameterise an identifier. Parsing at this boundary is what makes
/// that safe — the grammar is a strict allow-list, so no name that could
/// change the meaning of a statement can reach one.
/// </para>
/// </summary>
public interface IProvisionedFabSource
{
    /// <summary>
    /// Every fab whose name is usable, deduplicated, in no particular order.
    ///
    /// <para>
    /// <b>Throws</b> rather than returning an empty list when the registry
    /// cannot be reached or yields nothing usable (FR-011). "There are no
    /// fabs" and "I could not tell" are indistinguishable from inside this
    /// process and mean opposite things — and treating the second as the first
    /// would provision nothing while reporting success, which is the silence
    /// this feature exists to end.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<FabIdentifier>> GetFabsAsync(CancellationToken cancellationToken);
}
