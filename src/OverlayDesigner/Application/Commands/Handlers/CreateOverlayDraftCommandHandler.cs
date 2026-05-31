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
        var (name, label, createdBy) = command;

        Option<Overlay> existing = await overlays
            .GetByNameAsync(name, cancellationToken)
            .ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<OverlayIdentifier, CreateOverlayDraftError>.Failure(
                new CreateOverlayDraftError.OverlayNameTaken(name.Value));
        }

        Overlay overlay = Overlay.CreateDraft(name, label, createdBy, clock);
        overlays.Add(overlay);
        await overlays.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Created overlay {Overlay} '{Name}' (Draft) by {Operator}.",
            overlay.Id, name, createdBy);

        return Result<OverlayIdentifier, CreateOverlayDraftError>.Success(overlay.Id);
    }
}
