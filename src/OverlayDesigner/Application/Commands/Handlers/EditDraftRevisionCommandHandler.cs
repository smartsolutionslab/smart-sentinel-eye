using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class EditDraftRevisionCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<EditDraftRevisionCommandHandler> logger)
    : ICommandHandler<EditDraftRevisionCommand, Result<OverlayRevisionNumber, EditDraftRevisionError>>
{
    public async Task<Result<OverlayRevisionNumber, EditDraftRevisionError>> HandleAsync(
        EditDraftRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (overlayIdentifier, revisionNumber, label) = command;

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;
        Revision? revision = overlay.Revisions.SingleOrDefault(candidate => candidate.Number == revisionNumber);
        if (revision is null)
        {
            return Result<OverlayRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.OverlayRevisionNotFound(
                    overlayIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != OverlayRevisionState.Draft)
        {
            return Result<OverlayRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.NotADraft(revision.State.Value));
        }

        overlay.EditDraft(revisionNumber, label, clock);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        Log.EditedDraftRevision(logger, revisionNumber, overlay.Id);

        return Result<OverlayRevisionNumber, EditDraftRevisionError>.Success(revisionNumber);
    }
}
