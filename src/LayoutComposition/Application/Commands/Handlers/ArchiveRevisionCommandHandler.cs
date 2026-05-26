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

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(command.Layout, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<LayoutRevisionNumber, ArchiveRevisionError>.Failure(
                new ArchiveRevisionError.LayoutNotFound(command.Layout.Value));
        }

        Layout layout = found.Value;
        if (!layout.Revisions.Any(r => r.Number == command.RevisionNumber))
        {
            return Result<LayoutRevisionNumber, ArchiveRevisionError>.Failure(
                new ArchiveRevisionError.LayoutRevisionNotFound(
                    command.Layout.Value, command.RevisionNumber.Value));
        }

        layout.ArchiveRevision(command.RevisionNumber, command.ArchivedBy, clock);
        await layouts.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Archived layout {Layout} revision {Revision} by {Operator}.",
            layout.Id, command.RevisionNumber, command.ArchivedBy);

        return Result<LayoutRevisionNumber, ArchiveRevisionError>.Success(command.RevisionNumber);
    }
}
