using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;

public sealed class CreateOverlayDraftCommandHandler(
    IOverlayRepository overlays,
    IClock clock,
    ILogger<CreateOverlayDraftCommandHandler> logger)
    : ICommandHandler<CreateOverlayDraftCommand, Result<OverlayIdentifier, CreateOverlayDraftError>>
{
    public async Task<Result<OverlayIdentifier, CreateOverlayDraftError>> HandleAsync(
        CreateOverlayDraftCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (OverlayName? name, IReadOnlyList<OverlayElement> elements, OperatorIdentifier createdBy) = command;

        Option<ElementSetViolation> violation = Overlay.ValidateElements(elements);
        if (violation.HasValue)
        {
            return Failure(CreateOverlayDraftFailures.FromViolation(violation.Value, elements.Count));
        }

        Option<Overlay> existing = await overlays
            .GetByNameAsync(name, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(CreateOverlayDraftFailures.OverlayNameTaken(name.Value));
        }

        Overlay overlay = Overlay.CreateDraft(name, elements, createdBy, clock);
        overlays.Add(overlay);
        await overlays.SaveAsync(cancellationToken);

        logger.CreatedOverlay(overlay.Id, name, createdBy);

        return Success(overlay.Id);
    }
}
