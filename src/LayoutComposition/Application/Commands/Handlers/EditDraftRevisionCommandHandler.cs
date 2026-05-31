using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class EditDraftRevisionCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<EditDraftRevisionCommandHandler> log)
    : ICommandHandler<EditDraftRevisionCommand, Result<LayoutRevisionNumber, EditDraftRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, EditDraftRevisionError>> HandleAsync(
        EditDraftRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (layoutIdentifier, revisionNumber, camera, overlay) = command;

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(layoutIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;
        Revision? revision = layout.Revisions.SingleOrDefault(r => r.Number == revisionNumber);
        if (revision is null)
        {
            return Result<LayoutRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.LayoutRevisionNotFound(
                    layoutIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != LayoutRevisionState.Draft)
        {
            return Result<LayoutRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.NotADraft(revision.State.Value));
        }

        layout.EditDraft(revisionNumber, camera, clock);
        if (overlay.ShouldChange)
        {
            layout.AttachOverlay(revisionNumber, overlay.Value, clock);
        }
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Edited draft revision {Revision} on layout {Layout}.",
            revisionNumber, layout.Id);

        return Result<LayoutRevisionNumber, EditDraftRevisionError>.Success(revisionNumber);
    }
}
