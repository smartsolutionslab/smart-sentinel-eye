using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// Returns a declared <c>(fab, Source)</c> pair to undeclared (spec 317,
/// #2325, FR-013, US4) — the way back <see cref="ChangeSourceModeCommand"/>
/// has no equivalent for. <c>Fab</c> is the single fab resolved for this
/// write (ADR-0114), mirroring <see cref="ChangeSourceModeCommand"/>'s own
/// load-bearing order: the lookup runs before the version gate, so an
/// undeclare aimed at an undeclared pair and a stale undeclare aimed at a
/// declared one answer differently — 404 versus 409.
/// </summary>
public sealed record UndeclareSourceModeCommand(
    FabIdentifier Fab, Source Source, int ExpectedVersion, OperatorIdentifier UndeclaredBy)
    : ICommand<Result<SourceModeIdentifier, UndeclareSourceModeError>>;
