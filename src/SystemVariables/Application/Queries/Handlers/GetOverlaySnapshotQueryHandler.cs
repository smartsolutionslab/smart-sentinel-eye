using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

public sealed class GetOverlaySnapshotQueryHandler(IReverseIndex reverseIndex, IVariableRepository variables, IResolver resolver)
    : IQueryHandler<GetOverlaySnapshotQuery, Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>>
{
    public async Task<Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>> HandleAsync(GetOverlaySnapshotQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        string? labelText = reverseIndex.LookupLabelText(query.OverlayIdentifier);
        if (labelText is null)
        {
            return Failure(GetOverlaySnapshotFailures.OverlayNotInReverseIndex(query.OverlayIdentifier));
        }

        IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot = await BuildSnapshotAsync(query.Fabs, labelText, cancellationToken);

        string resolvedText = resolver.Resolve(labelText, snapshot);
        long version = reverseIndex.CurrentVersionFor(query.OverlayIdentifier);

        return Success(new ResolvedOverlaySnapshotDto(query.OverlayIdentifier, resolvedText, version));
    }

    private async Task<IReadOnlyDictionary<string, VariableSnapshotEntry>> BuildSnapshotAsync(
        IReadOnlyList<FabIdentifier> fabs, string labelText, CancellationToken cancellationToken)
    {
        Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);
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
            Option<Variable> found = await FindInAnyFabAsync(fabs, parsed, cancellationToken);
            if (!found.HasValue)
            {
                continue;
            }

            Variable variable = found.Value;
            if (variable.State == VariableState.Archived)
            {
                continue;
            }

            if (variable.Value is VariableValue.Unset)
            {
                continue;
            }

            snapshot[name] = new VariableSnapshotEntry(variable.Value, variable.BooleanLabels);
        }
        return snapshot;
    }

    /// <summary>
    /// The first fab, by name, that defines this variable. The list is
    /// whatever the query resolved to: one fab when the caller named one,
    /// otherwise every fab in its token — which may be several for any
    /// caller, kiosk included, since a principal's fabs come from its token
    /// and not from what it is displaying. The ordering makes that case
    /// arbitrary-but-stable rather than a refusal, on a read that returns
    /// rendered text rather than a row to act on.
    /// </summary>
    private async Task<Option<Variable>> FindInAnyFabAsync(
        IReadOnlyList<FabIdentifier> fabs, VariableName name, CancellationToken cancellationToken)
    {
        foreach (FabIdentifier fab in fabs.OrderBy(candidate => candidate.Value, StringComparer.Ordinal))
        {
            Option<Variable> found = await variables.GetByNameAsync(fab, name, cancellationToken);
            if (found.HasValue)
            {
                return found;
            }
        }

        return Option<Variable>.None;
    }
}
