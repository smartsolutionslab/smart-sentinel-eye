using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

public sealed record ArchiveRevisionCommand(
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    OperatorIdentifier ArchivedBy)
    : ICommand<Result<LayoutRevisionNumber, ArchiveRevisionError>>;
