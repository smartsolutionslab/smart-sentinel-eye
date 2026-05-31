using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class PublishRevisionCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<PublishRevisionCommandHandler> logger)
    : ICommandHandler<PublishRevisionCommand, Result<LayoutRevisionNumber, PublishRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, PublishRevisionError>> HandleAsync(
        PublishRevisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (layoutIdentifier, revisionNumber, publishedBy) = command;

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(layoutIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;
        Revision? revision = layout.Revisions.SingleOrDefault(candidate => candidate.Number == revisionNumber);
        if (revision is null)
        {
            return Result<LayoutRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.LayoutRevisionNotFound(
                    layoutIdentifier.Value, revisionNumber.Value));
        }
        if (revision.State != LayoutRevisionState.Draft)
        {
            return Result<LayoutRevisionNumber, PublishRevisionError>.Failure(
                new PublishRevisionError.InvalidStateTransition(revision.State.Value));
        }

        layout.Publish(revisionNumber, publishedBy, clock);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        Log.PublishedRevision(logger, layout.Id, revisionNumber, publishedBy);

        return Result<LayoutRevisionNumber, PublishRevisionError>.Success(revisionNumber);
    }
}
