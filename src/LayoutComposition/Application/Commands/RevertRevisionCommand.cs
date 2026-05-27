using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Reverts a Published revision to Draft so the admin can edit it
/// without spawning a new revision (spec 003 FR-003). Raises an
/// Archived domain event so connected kiosks force-disconnect.
/// </summary>
public sealed record RevertRevisionCommand(
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    OperatorIdentifier RevertedBy)
    : ICommand<Result<LayoutRevisionNumber, RevertRevisionError>>;
