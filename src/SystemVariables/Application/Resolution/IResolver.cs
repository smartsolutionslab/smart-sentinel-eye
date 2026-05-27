using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// Snapshot of a single variable: its current typed value and (for
/// Boolean) the configured truthy/falsy labels. <c>Value</c> is
/// guaranteed non-<see cref="VariableValue.Unset"/>; the resolver
/// uses presence-in-the-snapshot as the "is set" signal.
/// </summary>
public sealed record VariableSnapshotEntry(
    VariableValue Value,
    BooleanLabels? BooleanLabels);

/// <summary>
/// Pure overlay-text resolver. Walks the label, substitutes every
/// well-formed placeholder whose variable is present in the snapshot
/// and not <see cref="VariableValue.Unset"/>; everything else is left
/// literal (spec 005 FR-011).
/// </summary>
public interface IResolver
{
    /// <summary>
    /// Resolve every placeholder in <paramref name="labelText"/> using
    /// the provided snapshot. <paramref name="snapshot"/> only contains
    /// non-Archived, non-Unset variables — its absence is treated as
    /// "render literal placeholder" per FR-011.
    /// </summary>
    string Resolve(string labelText, IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot);
}
