using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class EditDraftRevisionCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<EditDraftRevisionCommandHandler> log)
    : ICommandHandler<EditDraftRevisionCommand, Result<OverlayRevisionNumber, EditDraftRevisionError>>
{
    public async Task<Result<OverlayRevisionNumber, EditDraftRevisionError>> HandleAsync(
        EditDraftRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(command.Overlay, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.OverlayNotFound(command.Overlay.Value));
        }

        Overlay overlay = found.Value;
        Revision? revision = overlay.Revisions.SingleOrDefault(r => r.Number == command.RevisionNumber);
        if (revision is null)
        {
            return Result<OverlayRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.OverlayRevisionNotFound(
                    command.Overlay.Value, command.RevisionNumber.Value));
        }
        if (revision.State != OverlayRevisionState.Draft)
        {
            return Result<OverlayRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.NotADraft(revision.State.Value));
        }

        overlay.EditDraft(command.RevisionNumber, command.Label, clock);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Edited draft revision {Revision} on overlay {Overlay}.",
            command.RevisionNumber, overlay.Id);

        return Result<OverlayRevisionNumber, EditDraftRevisionError>.Success(command.RevisionNumber);
    }
}
