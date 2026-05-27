using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class ArchiveRevisionCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<ArchiveRevisionCommandHandler> log)
    : ICommandHandler<ArchiveRevisionCommand, Result<OverlayRevisionNumber, ArchiveRevisionError>>
{
    public async Task<Result<OverlayRevisionNumber, ArchiveRevisionError>> HandleAsync(
        ArchiveRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(command.Overlay, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, ArchiveRevisionError>.Failure(
                new ArchiveRevisionError.OverlayNotFound(command.Overlay.Value));
        }

        Overlay overlay = found.Value;
        if (!overlay.Revisions.Any(r => r.Number == command.RevisionNumber))
        {
            return Result<OverlayRevisionNumber, ArchiveRevisionError>.Failure(
                new ArchiveRevisionError.OverlayRevisionNotFound(
                    command.Overlay.Value, command.RevisionNumber.Value));
        }

        overlay.ArchiveRevision(command.RevisionNumber, command.ArchivedBy, clock);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Archived overlay {Overlay} revision {Revision} by {Operator}.",
            overlay.Id, command.RevisionNumber, command.ArchivedBy);

        return Result<OverlayRevisionNumber, ArchiveRevisionError>.Success(command.RevisionNumber);
    }
}
