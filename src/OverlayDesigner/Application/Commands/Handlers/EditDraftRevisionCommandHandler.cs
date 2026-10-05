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
        Ensure.That(command).IsNotNull();
        (OverlayIdentifier overlayIdentifier, OverlayRevisionNumber revisionNumber, IReadOnlyList<OverlayElement> elements, int expectedVersion) = command;

        Option<ElementSetViolation> violation = Overlay.ValidateElements(elements);
        if (violation.HasValue)
        {
            return Failure(EditDraftRevisionFailures.FromViolation(violation.Value, elements.Count));
        }

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(overlayIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(EditDraftRevisionFailures.OverlayNotFound(overlayIdentifier.Value));
        }

        Overlay overlay = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (overlay.Version != expectedVersion)
        {
            return Failure(EditDraftRevisionFailures.OverlayRevisionStale(overlayIdentifier.Value, expectedVersion, overlay.Version));
        }
        Revision? revision = overlay.Revisions.SingleOrDefault(candidate => candidate.Number == revisionNumber);
        if (revision is null)
        {
            return Failure(EditDraftRevisionFailures.OverlayRevisionNotFound(
                    overlayIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != OverlayRevisionState.Draft)
        {
            return Failure(EditDraftRevisionFailures.NotADraft(revision.State.Value));
        }

        overlay.EditDraft(revisionNumber, elements, clock);
        await overlays.SaveAsync(cancellationToken);

        logger.EditedDraftRevision(revisionNumber, overlay.Id);

        return Success(revisionNumber);
    }
}
