using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class ArchiveRevisionCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<ArchiveRevisionCommandHandler> log)
    : ICommandHandler<ArchiveRevisionCommand, Result<LayoutRevisionNumber, ArchiveRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, ArchiveRevisionError>> HandleAsync(
        ArchiveRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (layoutIdentifier, revisionNumber, archivedBy) = command;

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(layoutIdentifier, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, ArchiveRevisionError>.Failure(
                new ArchiveRevisionError.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;
        if (!layout.Revisions.Any(r => r.Number == revisionNumber))
        {
            return Result<LayoutRevisionNumber, ArchiveRevisionError>.Failure(
                new ArchiveRevisionError.LayoutRevisionNotFound(
                    layoutIdentifier.Value, revisionNumber.Value));
        }

        layout.ArchiveRevision(revisionNumber, archivedBy, clock);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Archived layout {Layout} revision {Revision} by {Operator}.",
            layout.Id, revisionNumber, archivedBy);

        return Result<LayoutRevisionNumber, ArchiveRevisionError>.Success(revisionNumber);
    }
}
