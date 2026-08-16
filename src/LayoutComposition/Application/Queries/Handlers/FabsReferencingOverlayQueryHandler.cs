using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;

/// <summary>
/// Answers "which fabs are told about this overlay" (spec 017 FR-010): the
/// fabs that have a <b>published</b> layout whose tiles carry it.
///
/// <para>
/// This is the whole of Half B's mechanism, and it deliberately adds no
/// state. An overlay has no fab and must not gain one (ADR-0115) — it is a
/// fab-neutral template that two plants may legitimately share. So the answer
/// is derived from what references it, recomputed per frame, which means
/// archiving the last referencing layout narrows the audience with nothing to
/// invalidate.
/// </para>
///
/// <para>
/// Not an <c>IQueryHandler</c>: it answers a question the broadcast path asks
/// of itself rather than one an operator asks over HTTP, so it takes no
/// caller and returns no <c>Result</c>.
/// </para>
/// </summary>
public sealed class FabsReferencingOverlayQueryHandler(ILayoutQuerySource layouts)
{
    public async Task<IReadOnlyList<FabIdentifier>> HandleAsync(
        Guid overlay, CancellationToken cancellationToken)
    {
        OverlayIdentifier identifier = OverlayIdentifier.From(overlay);

        // Published only (FR-013). A draft's tiles do not count: a fab whose
        // sole use of an overlay is unpublished displays it nowhere, and
        // telling it what the overlay says would widen the audience past what
        // is actually on screen.
        return await layouts.Layouts
            .Where(layout => layout.Revisions.Any(revision =>
                revision.State == LayoutRevisionState.Published &&
                revision.Tiles.Any(tile => tile.OverlayValue == identifier)))
            .Select(layout => layout.Fab)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
