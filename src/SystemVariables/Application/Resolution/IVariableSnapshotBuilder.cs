using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// The resolution policy shared by <c>GetOverlaySnapshotQueryHandler</c> and
/// <c>ResolveOverlayTextQueryHandler</c> (spec 148 plan.md "The extraction") —
/// the fab search order, the three skip rules (Unknown / Archived / Unset)
/// and the per-placeholder ordinal tiebreak all live behind this one method
/// for those two callers. Two push-side domain event handlers
/// (<c>VariableValueChangedDomainEventHandler</c>,
/// <c>VariableArchivedDomainEventHandler</c>) still carry their own,
/// single-fab copy of the same rules (spec 148 plan.md "Follow-ups") — this
/// is not yet the only place the policy lives.
/// </summary>
public interface IVariableSnapshotBuilder
{
    /// <summary>
    /// Every name <c>PlaceholderParser.ExtractNames</c> finds in
    /// <paramref name="labelText"/>, each resolved against
    /// <paramref name="fabs"/> per ADR-0115: the first fab (by ordinal fab
    /// name) that defines the name wins, arbitrary but stable.
    /// </summary>
    Task<IReadOnlyList<PlaceholderResolution>> BuildAsync(
        IReadOnlyList<FabIdentifier> fabs, string labelText, CancellationToken cancellationToken);
}
