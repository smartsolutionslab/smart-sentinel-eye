using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class RevertRevisionCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<RevertRevisionCommandHandler> log)
    : ICommandHandler<RevertRevisionCommand, Result<OverlayRevisionNumber, RevertRevisionError>>
{
    public async Task<Result<OverlayRevisionNumber, RevertRevisionError>> HandleAsync(
        RevertRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(command.Overlay, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.OverlayNotFound(command.Overlay.Value));
        }

        Overlay overlay = found.Value;
        Revision? revision = overlay.Revisions.SingleOrDefault(r => r.Number == command.RevisionNumber);
        if (revision is null)
        {
            return Result<OverlayRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.OverlayRevisionNotFound(
                    command.Overlay.Value, command.RevisionNumber.Value));
        }
        if (revision.State != OverlayRevisionState.Published)
        {
            return Result<OverlayRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.NotPublished(revision.State.Value));
        }

        overlay.Revert(command.RevisionNumber, command.RevertedBy, clock);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Reverted revision {Revision} on overlay {Overlay} to Draft by {Operator}.",
            command.RevisionNumber, overlay.Id, command.RevertedBy);

        return Result<OverlayRevisionNumber, RevertRevisionError>.Success(command.RevisionNumber);
    }
}
