using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class ArchiveRevisionCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<ArchiveRevisionCommandHandler> logger)
    : ICommandHandler<ArchiveRevisionCommand, Result<OverlayRevisionNumber, ArchiveRevisionError>>
{
    public async Task<Result<OverlayRevisionNumber, ArchiveRevisionError>> HandleAsync(
        ArchiveRevisionCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (OverlayIdentifier overlayIdentifier, OverlayRevisionNumber revisionNumber, OperatorIdentifier archivedBy, int expectedVersion) = command;

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(ArchiveRevisionFailures.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (overlay.Version != expectedVersion)
        {
            return Failure(ArchiveRevisionFailures.OverlayRevisionStale(overlayIdentifier.Value, expectedVersion, overlay.Version));
        }
        if (overlay.Revisions.All(revision => revision.Number != revisionNumber))
        {
            return Failure(ArchiveRevisionFailures.OverlayRevisionNotFound(
                    overlayIdentifier.Value, revisionNumber.Value));
        }

        overlay.ArchiveRevision(revisionNumber, archivedBy, clock);
        await overlays.SaveAsync(cancellationToken);

        logger.ArchivedRevision(overlay.Id, revisionNumber, archivedBy);

        return Success(revisionNumber);
    }
}
