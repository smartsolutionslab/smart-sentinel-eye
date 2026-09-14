using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

public sealed class GetOverlaySnapshotQueryHandler(IReverseIndex reverseIndex, IVariableRepository variables, IResolver resolver)
    : IQueryHandler<GetOverlaySnapshotQuery, Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>>
{
    // Constructed from the injected repository rather than injected itself,
    // so this handler's public constructor stays exactly
    // (IReverseIndex, IVariableRepository, IResolver) — a shape
    // GetOverlaySnapshotQueryHandlerTests constructs directly at six call
    // sites and may not be edited (spec 148 plan.md "The extraction").
    private readonly VariableSnapshotBuilder builder = new(variables);

    public async Task<Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>> HandleAsync(GetOverlaySnapshotQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        string? labelText = reverseIndex.LookupLabelText(query.OverlayIdentifier);
        if (labelText is null)
        {
            return Failure(GetOverlaySnapshotFailures.OverlayNotInReverseIndex(query.OverlayIdentifier));
        }

        IReadOnlyList<PlaceholderResolution> resolutions = await builder.BuildAsync(query.Fabs, labelText, cancellationToken);

        Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);
        foreach (PlaceholderResolution resolution in resolutions)
        {
            if (resolution is { Outcome: PlaceholderOutcome.Resolved, Entry: { } entry })
            {
                snapshot[resolution.Name] = entry;
            }
        }

        string resolvedText = resolver.Resolve(labelText, snapshot);
        long version = reverseIndex.CurrentVersionFor(query.OverlayIdentifier);

        return Success(new ResolvedOverlaySnapshotDto(query.OverlayIdentifier, resolvedText, version));
    }
}
