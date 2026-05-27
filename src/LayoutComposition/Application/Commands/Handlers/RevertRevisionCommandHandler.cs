using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class RevertRevisionCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<RevertRevisionCommandHandler> log)
    : ICommandHandler<RevertRevisionCommand, Result<LayoutRevisionNumber, RevertRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, RevertRevisionError>> HandleAsync(
        RevertRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(command.Layout, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.LayoutNotFound(command.Layout.Value));
        }

        Layout layout = found.Value;
        Revision? revision = layout.Revisions.SingleOrDefault(r => r.Number == command.RevisionNumber);
        if (revision is null)
        {
            return Result<LayoutRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.LayoutRevisionNotFound(
                    command.Layout.Value, command.RevisionNumber.Value));
        }
        if (revision.State != LayoutRevisionState.Published)
        {
            return Result<LayoutRevisionNumber, RevertRevisionError>.Failure(
                new RevertRevisionError.NotPublished(revision.State.Value));
        }

        layout.Revert(command.RevisionNumber, command.RevertedBy, clock);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Reverted revision {Revision} on layout {Layout} to Draft by {Operator}.",
            command.RevisionNumber, layout.Id, command.RevertedBy);

        return Result<LayoutRevisionNumber, RevertRevisionError>.Success(command.RevisionNumber);
    }
}
