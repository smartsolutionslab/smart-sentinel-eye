using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

/// <summary>
/// Publishes a Draft revision within an existing chain. Atomically
/// archives the previously-Published revision in the same UoW (FR-003).
/// </summary>
public sealed record PublishRevisionCommand(
    OverlayIdentifier Overlay,
    OverlayRevisionNumber RevisionNumber,
    OperatorIdentifier PublishedBy)
    : ICommand<Result<OverlayRevisionNumber, PublishRevisionError>>;
