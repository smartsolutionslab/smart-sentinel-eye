using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Domain.Variable.Events;

/// <summary>
/// In-process domain event raised when a variable's value changes.
/// Translated by the Application layer into a
/// <c>SystemVariableValueChangedV1</c> integration event + a fan-out
/// to every overlay in the reverse-index entry for this variable's
/// name (spec 005 FR-013).
/// </summary>
public sealed record VariableValueChangedDomainEvent(
    VariableIdentifier Variable,
    VariableName Name,
    VariableType Type,
    VariableValue Value,
    DateTimeOffset ChangedAt,
    OperatorIdentifier ChangedBy,
    BooleanLabels? BooleanLabels) : IDomainEvent;
