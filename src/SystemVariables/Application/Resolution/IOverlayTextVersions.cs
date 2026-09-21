namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// Durable per-overlay version counter for
/// <c>ResolvedOverlayTextChangedV1.Version</c> and the snapshot's
/// <c>version</c> field (issue #2426). Replaces the process-lifetime
/// counter that used to live on <see cref="IReverseIndex"/> — a
/// restart of this service must not reset what a connected kiosk
/// already holds as its high-water mark (ADR-0145 §1).
///
/// <para>
/// Implemented by <c>OverlayTextVersionStore</c> (Postgres-backed,
/// mirroring <c>VariableValueRequestDedupStore</c>) — see
/// specs/202-a-version-that-survives-a-restart/plan.md §§2-4 for the
/// storage shape, the cutover floor and the fan-out atomicity argument.
/// </para>
/// </summary>
public interface IOverlayTextVersions
{
    /// <summary>
    /// Advances the version for every overlay in
    /// <paramref name="overlayIdentifiers"/> by exactly one and returns
    /// each overlay's new version, in a single round trip regardless of
    /// how many overlays are affected — the push fan-out calls this
    /// once per variable change, not once per overlay (plan.md §2). An
    /// overlay seen for the first time is issued the cutover floor
    /// (plan.md §3), never <c>1</c>. A duplicate identifier appearing
    /// more than once in <paramref name="overlayIdentifiers"/> is
    /// advanced only once; every duplicate resolves to the same
    /// returned version (plan.md's R3 — the caller may hand this a
    /// batch with a repeated identifier and must not see it fail).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, long>> AdvanceAsync(
        IReadOnlyCollection<Guid> overlayIdentifiers, CancellationToken cancellationToken);

    /// <summary>
    /// The current version for one overlay, without advancing it. Used
    /// by the snapshot read path. Returns <c>0</c> for an overlay that
    /// has never been advanced — distinct from the cutover floor, which
    /// is what a real first push carries.
    /// </summary>
    Task<long> CurrentAsync(Guid overlayIdentifier, CancellationToken cancellationToken);
}
