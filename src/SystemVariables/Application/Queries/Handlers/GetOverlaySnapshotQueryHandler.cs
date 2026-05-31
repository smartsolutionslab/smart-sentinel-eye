using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

public sealed class GetOverlaySnapshotQueryHandler(
    IReverseIndex reverseIndex,
    IVariableRepository variables,
    IResolver resolver)
    : IQueryHandler<GetOverlaySnapshotQuery, Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>>
{
    public async Task<Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>> HandleAsync(
        GetOverlaySnapshotQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        string? labelText = reverseIndex.LookupLabelText(query.OverlayIdentifier);
        if (labelText is null)
        {
            return Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>.Failure(
                new GetOverlaySnapshotError.OverlayNotInReverseIndex(query.OverlayIdentifier));
        }

        IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot =
            await BuildSnapshotAsync(labelText, cancellationToken).ConfigureAwait(false);

        string resolvedText = resolver.Resolve(labelText, snapshot);
        long version = reverseIndex.CurrentVersionFor(query.OverlayIdentifier);

        return Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>.Success(
            new ResolvedOverlaySnapshotDto(query.OverlayIdentifier, resolvedText, version));
    }

    private async Task<IReadOnlyDictionary<string, VariableSnapshotEntry>> BuildSnapshotAsync(
        string labelText, CancellationToken cancellationToken)
    {
        Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);
        foreach (string name in PlaceholderParser.ExtractNames(labelText))
        {
            VariableName parsed;
            try { parsed = VariableName.From(name); }
            catch (ArgumentException) { continue; }

            Option<Variable> found = await variables
                .GetByNameAsync(parsed, cancellationToken)
                .ConfigureAwait(false);
            if (!found.HasValue) continue;

            Variable variable = found.Value;
            if (variable.State == VariableState.Archived) continue;
            if (variable.Value is VariableValue.Unset) continue;

            snapshot[name] = new VariableSnapshotEntry(variable.Value, variable.BooleanLabels);
        }
        return snapshot;
    }
}
