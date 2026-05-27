using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Creates the first revision of a new logical Layout chain in Draft
/// state. Name must be unique across all non-Archived chains (FR-006).
/// </summary>
public sealed record CreateLayoutDraftCommand(
    LayoutName Name,
    CameraIdentifier Camera,
    OperatorIdentifier CreatedBy,
    OverlayIdentifier? Overlay = null)
    : ICommand<Result<LayoutIdentifier, CreateLayoutDraftError>>;
