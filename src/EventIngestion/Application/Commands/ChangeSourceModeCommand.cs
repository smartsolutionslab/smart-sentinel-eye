using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// <c>Fab</c> is the single fab resolved for this write (ADR-0114). The
/// handler's order is load-bearing (plan.md §6.4): the lookup runs before the
/// version gate, so a change aimed at an undeclared pair and a stale change
/// aimed at a declared one answer differently — 404 versus 409, mirroring
/// <c>RetireEventTypeCommand</c>.
/// </summary>
public sealed record ChangeSourceModeCommand(
    FabIdentifier Fab, Source Source, EventTypeMode Mode, int ExpectedVersion, OperatorIdentifier ChangedBy)
    : ICommand<Result<SourceModeIdentifier, ChangeSourceModeError>>;
