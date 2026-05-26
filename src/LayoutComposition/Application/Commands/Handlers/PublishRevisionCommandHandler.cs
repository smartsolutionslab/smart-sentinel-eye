using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class PublishRevisionCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<PublishRevisionCommandHandler> log)
    : ICommandHandler<PublishRevisionCommand, Result<LayoutRevisionNumber, PublishRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, PublishRevisionError>> HandleAsync(
        PublishRevisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(command.Layout, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.LayoutNotFound(command.Layout.Value));
        }

        Layout layout = found.Value;
        Revision? revision = layout.Revisions.SingleOrDefault(r => r.Number == command.RevisionNumber);
        if (revision is null)
        {
            return Result<LayoutRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.LayoutRevisionNotFound(
                    command.Layout.Value, command.RevisionNumber.Value));
        }
        if (revision.State != LayoutRevisionState.Draft)
        {
            return Result<LayoutRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.InvalidStateTransition(revision.State.Value));
        }

        layout.Publish(command.RevisionNumber, command.PublishedBy, clock);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Published layout {Layout} revision {Revision} by {Operator}.",
            layout.Id, command.RevisionNumber, command.PublishedBy);

        return Result<LayoutRevisionNumber, PublishRevisionError>.Success(command.RevisionNumber);
    }
}
