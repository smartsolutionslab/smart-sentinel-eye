using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Branches a new Draft revision off the chain's current Published
/// revision (spec 003 US4). Pre-fills the camera from the prior
/// revision so the admin edits a known-good baseline.
/// </summary>
public sealed record BranchDraftRevisionCommand(
    LayoutIdentifier Layout,
    OperatorIdentifier BranchedBy)
    : ICommand<Result<LayoutRevisionNumber, BranchDraftRevisionError>>;
