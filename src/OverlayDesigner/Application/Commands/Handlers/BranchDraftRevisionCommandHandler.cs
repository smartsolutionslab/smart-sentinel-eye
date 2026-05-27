using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class BranchDraftRevisionCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<BranchDraftRevisionCommandHandler> log)
    : ICommandHandler<BranchDraftRevisionCommand, Result<OverlayRevisionNumber, BranchDraftRevisionError>>
{
    public async Task<Result<OverlayRevisionNumber, BranchDraftRevisionError>> HandleAsync(
        BranchDraftRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Overlay> found = await overlays
            .GetByIdentifierAsync(command.Overlay, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<OverlayRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.OverlayNotFound(command.Overlay.Value));
        }

        Overlay overlay = found.Value;
        if (!overlay.Revisions.Any(r => r.State == OverlayRevisionState.Published))
        {
            return Result<OverlayRevisionNumber, BranchDraftRevisionError>.Failure(
                new BranchDraftRevisionError.NoPublishedRevisionToBranchFrom(command.Overlay.Value));
        }

        Revision branched = overlay.BranchDraft(command.BranchedBy, clock);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Branched draft revision {Revision} on overlay {Overlay} by {Operator}.",
            branched.Number, overlay.Id, command.BranchedBy);

        return Result<OverlayRevisionNumber, BranchDraftRevisionError>.Success(branched.Number);
    }
}
