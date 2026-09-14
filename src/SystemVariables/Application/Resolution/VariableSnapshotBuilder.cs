using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Resolution;

/// <summary>
/// The resolution loop extracted from
/// <c>GetOverlaySnapshotQueryHandler.BuildSnapshotAsync</c> +
/// <c>FindInAnyFabAsync</c> (spec 148 plan.md "The extraction") for its two
/// query-side callers, <c>GetOverlaySnapshotQueryHandler</c> and
/// <c>ResolveOverlayTextQueryHandler</c> — the fab search order, the three
/// skip rules and the per-placeholder ordinal tiebreak all live here for
/// both. It is not the only place this policy lives: the push-side domain
/// event handlers (<c>VariableValueChangedDomainEventHandler</c>,
/// <c>VariableArchivedDomainEventHandler</c>) each carry their own copy,
/// single-fab rather than multi-fab (spec 148 plan.md "Follow-ups").
/// </summary>
public sealed class VariableSnapshotBuilder(IVariableRepository variables) : IVariableSnapshotBuilder
{
    public async Task<IReadOnlyList<PlaceholderResolution>> BuildAsync(
        IReadOnlyList<FabIdentifier> fabs, string labelText, CancellationToken cancellationToken)
    {
        Ensure.That(fabs).IsNotNull();
        Ensure.That(labelText).IsNotNull();

        List<PlaceholderResolution> resolutions = [];
        foreach (string name in PlaceholderParser.ExtractNames(labelText))
        {
            VariableName parsed;
            try { parsed = VariableName.From(name); }
            catch (ArgumentException) { continue; }

            // The viewer's fab, not the overlay's: an overlay is a fab-neutral
            // template and what a placeholder is worth belongs to the plant
            // looking at the screen (ADR-0115). Where the query named no fab
            // and the caller holds several, the first by fab name wins —
            // arbitrary but stable. A caller who needs one plant's answer says
            // which; the kiosk does, from the wall it displays (ADR-0145).
            (Option<Variable> found, FabIdentifier? answeringFab) =
                await FindInAnyFabAsync(fabs, parsed, cancellationToken);
            if (!found.HasValue)
            {
                // GetByNameAsync already excludes Archived rows (FR-005: a
                // released name is free for re-use), so a miss here means
                // either the name never existed or it exists only archived —
                // and only an extra, archived-inclusive check can tell those
                // apart. Checked here rather than in the same call as the
                // live lookup because that would let an archived row in an
                // earlier-ordinal fab mask a live one in a later fab.
                PlaceholderOutcome outcome = await ExistsArchivedInAnyFabAsync(fabs, parsed, cancellationToken)
                    ? PlaceholderOutcome.Archived
                    : PlaceholderOutcome.Unknown;
                resolutions.Add(new PlaceholderResolution(name, outcome, null, null));
                continue;
            }

            Variable variable = found.Value;
            if (variable.Value is VariableValue.Unset)
            {
                // The fab it was found in, same as a Resolved row (plan.md:170)
                // — the answering fab is already in hand here, unlike the
                // not-found path below where Archived has none to give.
                resolutions.Add(new PlaceholderResolution(name, PlaceholderOutcome.Unset, answeringFab, null));
                continue;
            }

            resolutions.Add(new PlaceholderResolution(
                name,
                PlaceholderOutcome.Resolved,
                answeringFab,
                new VariableSnapshotEntry(variable.Value, variable.BooleanLabels)));
        }
        return resolutions;
    }

    /// <summary>
    /// The first fab, by name, that defines this variable, plus which fab
    /// that was — the caller needs the fab itself for
    /// <see cref="PlaceholderResolution.Fab"/>, not only the variable it
    /// found. The list is whatever the query resolved to: one fab when the
    /// caller named one, otherwise every fab in its token — which may be
    /// several for any caller, kiosk included, since a principal's fabs come
    /// from its token and not from what it is displaying. The ordering makes
    /// that case arbitrary-but-stable rather than a refusal, on a read that
    /// returns rendered text rather than a row to act on.
    /// </summary>
    private async Task<(Option<Variable> Variable, FabIdentifier? Fab)> FindInAnyFabAsync(
        IReadOnlyList<FabIdentifier> fabs, VariableName name, CancellationToken cancellationToken)
    {
        foreach (FabIdentifier fab in fabs.OrderBy(candidate => candidate.Value, StringComparer.Ordinal))
        {
            Option<Variable> found = await variables.GetByNameAsync(fab, name, cancellationToken);
            if (found.HasValue)
            {
                return (found, fab);
            }
        }

        return (Option<Variable>.None, null);
    }

    /// <summary>
    /// Whether this name exists, archived, in any of the caller's fabs — the
    /// fallback that distinguishes <see cref="PlaceholderOutcome.Archived"/>
    /// from <see cref="PlaceholderOutcome.Unknown"/> once
    /// <see cref="FindInAnyFabAsync"/> has already established no fab holds
    /// a live one. <see cref="PlaceholderResolution.Fab"/> is null for both
    /// outcomes, so which fab answered does not matter here.
    /// </summary>
    private async Task<bool> ExistsArchivedInAnyFabAsync(
        IReadOnlyList<FabIdentifier> fabs, VariableName name, CancellationToken cancellationToken)
    {
        foreach (FabIdentifier fab in fabs)
        {
            if (await variables.ExistsIncludingArchivedAsync(fab, name, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
