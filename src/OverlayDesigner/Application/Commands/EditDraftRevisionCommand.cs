using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

/// <summary>
/// Mutates a Draft revision's Label in place (spec 004 FR-005). Edits
/// only valid on Draft revisions; Published revisions must Branch first
/// and then Edit the new Draft.
/// </summary>
public sealed record EditDraftRevisionCommand(
    OverlayIdentifier Overlay,
    OverlayRevisionNumber RevisionNumber,
    Label Label)
    : ICommand<Result<OverlayRevisionNumber, EditDraftRevisionError>>;
