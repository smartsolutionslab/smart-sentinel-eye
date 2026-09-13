using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// <c>Fabs</c> is the fabs the caller holds. The retire handler's order is
/// load-bearing (plan.md §6): the lookup over these fabs runs before the
/// version gate, so a retire aimed at another fab's row and a stale retire
/// aimed at the caller's own row answer differently — 404 versus 409.
/// </summary>
public sealed record RetireEventTypeCommand(
    IReadOnlyList<FabIdentifier> Fabs, Kind Kind, int ExpectedVersion, OperatorIdentifier RetiredBy)
    : ICommand<Result<RegisteredEventTypeIdentifier, RetireEventTypeError>>;
