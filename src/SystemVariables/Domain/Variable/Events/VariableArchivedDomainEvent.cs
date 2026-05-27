using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Domain.Variable.Events;

/// <summary>
/// In-process domain event raised on archive. Translated by the
/// Application layer into a <c>SystemVariableArchivedV1</c>
/// integration event + a fan-out re-resolving every affected overlay
/// (their literal placeholder is reinstated).
/// </summary>
public sealed record VariableArchivedDomainEvent(
    VariableIdentifier Variable,
    VariableName Name,
    DateTimeOffset ArchivedAt,
    OperatorIdentifier ArchivedBy) : IDomainEvent;
