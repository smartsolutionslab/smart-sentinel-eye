using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

public sealed class GetOverlaySnapshotQueryHandler(
    IReverseIndex reverseIndex, IVariableRepository variables, IResolver resolver, IOverlayTextVersions overlayTextVersions)
    : IQueryHandler<GetOverlaySnapshotQuery, Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>>
{
    // Constructed from the injected repository rather than injected itself,
    // so this handler's constructor grows only the one dependency this fix
    // genuinely needs (IOverlayTextVersions) rather than a second builder
    // seam. The four-parameter shape below is what
    // GetOverlaySnapshotQueryHandlerTests now constructs directly at its
    // call sites (issue #2426) — the spec-148 constraint this comment used
    // to describe was for a behaviour-*preserving* refactor; this change is
    // behaviour-changing and the six call sites were updated with it.
    private readonly VariableSnapshotBuilder builder = new(variables);

    public async Task<Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError>> HandleAsync(GetOverlaySnapshotQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        string? labelText = reverseIndex.LookupLabelText(query.OverlayIdentifier);
        if (labelText is null)
        {
            return Failure(GetOverlaySnapshotFailures.OverlayNotInReverseIndex(query.OverlayIdentifier));
        }

        // Issue #2426 Finding B / SC-7 -- read before resolving the text. A
        // version read before the text is a lower bound on the text's
        // freshness: a push committing while this request is in flight then
        // carries a strictly higher version, and the kiosk accepts it. Read
        // after, the snapshot could stamp stale text with the newer push's
        // version and the kiosk would drop that push as not-newer.
        long version = await overlayTextVersions.CurrentAsync(query.OverlayIdentifier, cancellationToken);

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

        return Success(new ResolvedOverlaySnapshotDto(query.OverlayIdentifier, resolvedText, version));
    }
}
