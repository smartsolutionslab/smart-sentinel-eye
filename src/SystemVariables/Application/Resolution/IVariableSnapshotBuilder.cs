using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// The resolution policy shared by <c>GetOverlaySnapshotQueryHandler</c> and
/// <c>ResolveOverlayTextQueryHandler</c> (spec 148 plan.md "The extraction").
/// One loop, two callers, zero policy duplication — the fab search order,
/// the three skip rules (Unknown / Archived / Unset) and the per-placeholder
/// ordinal tiebreak all live behind this one method.
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
