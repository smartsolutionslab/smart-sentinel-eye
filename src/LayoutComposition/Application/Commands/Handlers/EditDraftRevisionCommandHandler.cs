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

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(command.Layout, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.LayoutNotFound(command.Layout.Value));
        }

        Layout layout = found.Value;
        Revision? revision = layout.Revisions.SingleOrDefault(r => r.Number == command.RevisionNumber);
        if (revision is null)
        {
            return Result<LayoutRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.LayoutRevisionNotFound(
                    command.Layout.Value, command.RevisionNumber.Value));
        }
        if (revision.State != LayoutRevisionState.Draft)
        {
            return Result<LayoutRevisionNumber, EditDraftRevisionError>.Failure(
                new EditDraftRevisionError.NotADraft(revision.State.Value));
        }

        layout.EditDraft(command.RevisionNumber, command.Camera, clock);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Edited draft revision {Revision} on layout {Layout}.",
            command.RevisionNumber, layout.Id);

        return Result<LayoutRevisionNumber, EditDraftRevisionError>.Success(command.RevisionNumber);
    }
}
