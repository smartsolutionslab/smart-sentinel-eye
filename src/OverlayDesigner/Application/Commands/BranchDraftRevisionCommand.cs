using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

/// <summary>
/// Branches a new Draft revision off the chain's current Published
/// revision (spec 004 US4). Pre-fills the Label from the prior
/// revision so the admin edits a known-good baseline.
/// </summary>
public sealed record BranchDraftRevisionCommand(
    OverlayIdentifier Overlay,
    OperatorIdentifier BranchedBy)
    : ICommand<Result<OverlayRevisionNumber, BranchDraftRevisionError>>;
