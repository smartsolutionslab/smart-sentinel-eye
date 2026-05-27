using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Mutates a Draft revision's camera in place. v1 only edits the
/// camera; renaming the chain is deferred (see spec edge cases).
/// </summary>
public sealed record EditDraftRevisionCommand(
    LayoutIdentifier Layout,
    LayoutRevisionNumber RevisionNumber,
    CameraIdentifier Camera)
    : ICommand<Result<LayoutRevisionNumber, EditDraftRevisionError>>;
