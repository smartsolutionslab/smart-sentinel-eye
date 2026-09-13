using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// <c>Fab</c> is the fab the registering operator resolved to
/// (<c>EventIngestionFabResolution.ResolveWriteFabAsync</c>), not a field they
/// supply unchecked (spec 143 FR-003).
/// </summary>
public sealed record RegisterEventTypeCommand(
    FabIdentifier Fab, Kind Kind, OperatorIdentifier RegisteredBy)
    : ICommand<Result<RegisteredEventTypeIdentifier, RegisterEventTypeError>>;
