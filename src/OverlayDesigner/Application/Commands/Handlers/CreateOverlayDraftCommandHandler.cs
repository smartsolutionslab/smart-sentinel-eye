using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class CreateOverlayDraftCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<CreateOverlayDraftCommandHandler> log)
    : ICommandHandler<CreateOverlayDraftCommand, Result<OverlayIdentifier, CreateOverlayDraftError>>
{
    public async Task<Result<OverlayIdentifier, CreateOverlayDraftError>> HandleAsync(
        CreateOverlayDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Overlay> existing = await overlays
            .GetByNameAsync(command.Name, cancellationToken)
            .ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<OverlayIdentifier, CreateOverlayDraftError>.Failure(
                new CreateOverlayDraftError.OverlayNameTaken(command.Name.Value));
        }

        Overlay overlay = Overlay.CreateDraft(command.Name, command.Label, command.CreatedBy, clock);
        overlays.Add(overlay);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Created overlay {Overlay} '{Name}' (Draft) by {Operator}.",
            overlay.Id, command.Name, command.CreatedBy);

        return Result<OverlayIdentifier, CreateOverlayDraftError>.Success(overlay.Id);
    }
}
