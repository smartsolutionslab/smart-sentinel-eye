using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// <c>Fab</c> is the fab the declaring operator resolved to
/// (<c>EventIngestionFabResolution.ResolveWriteFabAsync</c>), not a field they
/// supply unchecked (spec.md FR-010, mirrors spec 143 FR-003).
/// </summary>
public sealed record DeclareSourceModeCommand(
    FabIdentifier Fab, Source Source, EventTypeMode Mode, OperatorIdentifier DeclaredBy)
    : ICommand<Result<SourceModeIdentifier, DeclareSourceModeError>>;
