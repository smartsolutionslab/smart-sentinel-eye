using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

/// <summary>
/// Reverts a Published revision to Draft so the admin can edit it
/// without spawning a new revision. Raises an Archived domain event so
/// kiosks rendering the overlay drop it until republished.
/// </summary>
public sealed record RevertRevisionCommand(
    OverlayIdentifier Overlay,
    OverlayRevisionNumber RevisionNumber,
    OperatorIdentifier RevertedBy)
    : ICommand<Result<OverlayRevisionNumber, RevertRevisionError>>;
