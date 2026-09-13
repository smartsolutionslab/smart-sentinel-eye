using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// <c>Fab</c> is the single fab resolved for this write (ADR-0114's write
/// path — inferred when the caller holds exactly one and names none, refused
/// as <c>EVENT_FAB_REQUIRED</c> when they hold several and name none). A
/// multi-fab caller who has registered the same kind in two fabs they hold
/// must not have the retire pick one nondeterministically, which a
/// plural-fabs lookup could. The handler's order is still load-bearing
/// (plan.md §6): the lookup runs before the version gate, so a retire aimed
/// at another fab's row and a stale retire aimed at the caller's own row
/// answer differently — 404 versus 409.
/// </summary>
public sealed record RetireEventTypeCommand(
    FabIdentifier Fab, Kind Kind, int ExpectedVersion, OperatorIdentifier RetiredBy)
    : ICommand<Result<RegisteredEventTypeIdentifier, RetireEventTypeError>>;
