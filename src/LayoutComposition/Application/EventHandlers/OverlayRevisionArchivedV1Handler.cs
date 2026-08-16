using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber on <see cref="OverlayRevisionArchivedV1"/> from
/// OverlayDesigner. Relays it onto the <c>/hubs/layouts</c> SignalR hub
/// via the broadcaster LayoutComposition owns (force-disconnect
/// semantics for kiosks rendering the archived overlay). See
/// <see cref="OverlayRevisionPublishedV1Handler"/> for the rationale.
/// </summary>
public sealed class OverlayRevisionArchivedV1Handler(
    ILayoutLifecycleBroadcaster broadcaster,
    FabsReferencingOverlayQueryHandler referencingFabs,
    ILogger<OverlayRevisionArchivedV1Handler> logger)
{
    public async Task Handle(OverlayRevisionArchivedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();

        var (overlay, revisionNumber, archivedAt, _, _) = message;

        // Same resolution as the published frame. Note the asymmetry this
        // creates and that FR-013 accepts: a fab whose only use of the overlay
        // is in a draft is not told it was archived, and finds the draft
        // broken at publish time.
        IReadOnlyList<FabIdentifier> fabs = await referencingFabs.HandleAsync(overlay, cancellationToken);

        await broadcaster.OverlayArchivedAsync(
            new OverlayLifecycleArchivedNotification(
                Fabs: fabs,
                Overlay: overlay,
                RevisionNumber: revisionNumber,
                ArchivedAt: archivedAt),
            cancellationToken);

        logger.BroadcastOverlayArchived(overlay, revisionNumber);
    }
}
