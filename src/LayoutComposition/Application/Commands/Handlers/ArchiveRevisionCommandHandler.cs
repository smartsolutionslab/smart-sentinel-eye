using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands.Handlers;

public sealed class ArchiveRevisionCommandHandler(
    ILayoutRepository layouts,
    IClock clock,
    ILogger<ArchiveRevisionCommandHandler> logger)
    : ICommandHandler<ArchiveRevisionCommand, Result<LayoutRevisionNumber, ArchiveRevisionError>>
{
    public async Task<Result<LayoutRevisionNumber, ArchiveRevisionError>> HandleAsync(
        ArchiveRevisionCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (LayoutIdentifier layoutIdentifier, LayoutRevisionNumber revisionNumber, OperatorIdentifier archivedBy, int expectedVersion) = command;

        Option<Layout> found = await layouts
            .GetByIdentifierAsync(layoutIdentifier, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(ArchiveRevisionFailures.LayoutNotFound(layoutIdentifier.Value));
        }

        Layout layout = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the chain that
        // has since moved. Checked before any mutation so nothing is applied
        // on top of stale intent.
        if (layout.Version != expectedVersion)
        {
            return Failure(ArchiveRevisionFailures.LayoutRevisionStale(layoutIdentifier.Value, expectedVersion, layout.Version));
        }
        if (layout.Revisions.All(revision => revision.Number != revisionNumber))
        {
            return Failure(ArchiveRevisionFailures.LayoutRevisionNotFound(
                    layoutIdentifier.Value, revisionNumber.Value));
        }

        layout.ArchiveRevision(revisionNumber, archivedBy, clock);
        await layouts.SaveAsync(cancellationToken);

        logger.ArchivedRevision(layout.Id, revisionNumber, archivedBy);

        return Success(revisionNumber);
    }
}
