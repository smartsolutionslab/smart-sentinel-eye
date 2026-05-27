using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

public sealed record ArchiveRevisionCommand(
    OverlayIdentifier Overlay,
    OverlayRevisionNumber RevisionNumber,
    OperatorIdentifier ArchivedBy)
    : ICommand<Result<OverlayRevisionNumber, ArchiveRevisionError>>;
