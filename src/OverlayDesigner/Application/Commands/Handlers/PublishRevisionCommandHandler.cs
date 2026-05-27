using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class PublishRevisionCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<PublishRevisionCommandHandler> log)
    : ICommandHandler<PublishRevisionCommand, Result<OverlayRevisionNumber, PublishRevisionError>>
{
    public async Task<Result<OverlayRevisionNumber, PublishRevisionError>> HandleAsync(
        PublishRevisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(command.Overlay, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.OverlayNotFound(command.Overlay.Value));
        }

        Overlay overlay = found.Value;
        Revision? revision = overlay.Revisions.SingleOrDefault(r => r.Number == command.RevisionNumber);
        if (revision is null)
        {
            return Result<OverlayRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.OverlayRevisionNotFound(
                    command.Overlay.Value, command.RevisionNumber.Value));
        }
        if (revision.State != OverlayRevisionState.Draft)
        {
            return Result<OverlayRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.InvalidStateTransition(revision.State.Value));
        }

        overlay.Publish(command.RevisionNumber, command.PublishedBy, clock);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Published overlay {Overlay} revision {Revision} by {Operator}.",
            overlay.Id, command.RevisionNumber, command.PublishedBy);

        return Result<OverlayRevisionNumber, PublishRevisionError>.Success(command.RevisionNumber);
    }
}
