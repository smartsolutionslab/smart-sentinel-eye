using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Publishes a Draft revision within an existing chain. Atomically
/// archives the previously-Published revision in the same UoW (FR-003).
/// </summary>
public sealed record PublishRevisionCommand(
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    OperatorIdentifier PublishedBy)
    : ICommand<Result<LayoutRevisionNumber, PublishRevisionError>>;
