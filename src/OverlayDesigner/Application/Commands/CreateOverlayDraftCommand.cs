using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

/// <summary>
/// Creates the first revision of a new logical Overlay chain in Draft
/// state. Name must be unique across all non-Archived chains.
/// </summary>
public sealed record CreateOverlayDraftCommand(
    OverlayName Name,
    Label Label,
    OperatorIdentifier CreatedBy)
    : ICommand<Result<OverlayIdentifier, CreateOverlayDraftError>>;
