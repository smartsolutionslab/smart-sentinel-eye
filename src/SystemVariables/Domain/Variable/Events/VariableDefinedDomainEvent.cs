using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Domain.Variable.Events;

/// <summary>
/// In-process domain event raised when a system variable is first
/// defined. The Application layer translates this into a
/// <c>SystemVariableDefinedV1</c> on the integration bus.
/// </summary>
public sealed record VariableDefinedDomainEvent(
    VariableIdentifier Variable,
    VariableName Name,
    VariableType Type,
    DateTimeOffset DefinedAt,
    OperatorIdentifier DefinedBy) : IDomainEvent;
