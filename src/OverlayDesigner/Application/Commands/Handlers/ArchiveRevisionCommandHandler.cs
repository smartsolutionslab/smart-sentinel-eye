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
        ArchiveRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (overlayIdentifier, revisionNumber, archivedBy) = command;

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, ArchiveRevisionError>.Failure(
                new ArchiveRevisionError.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;
        if (!overlay.Revisions.Any(r => r.Number == revisionNumber))
        {
            return Result<OverlayRevisionNumber, ArchiveRevisionError>.Failure(
                new ArchiveRevisionError.OverlayRevisionNotFound(
                    overlayIdentifier.Value, revisionNumber.Value));
        }

        overlay.ArchiveRevision(revisionNumber, archivedBy, clock);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Archived overlay {Overlay} revision {Revision} by {Operator}.",
            overlay.Id, revisionNumber, archivedBy);

        return Result<OverlayRevisionNumber, ArchiveRevisionError>.Success(revisionNumber);
    }
}
