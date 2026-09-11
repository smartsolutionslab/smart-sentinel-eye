using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Domain.Variable.Events;

/// <summary>
/// In-process domain event raised when a variable's value changes.
/// Translated by the Application layer into a
/// <c>SystemVariableValueChangedV1</c> integration event + a fan-out
/// to every overlay in the reverse-index entry for this variable's
/// name (spec 005 FR-013).
/// </summary>
/// <param name="RootIngestedAt">
/// When the plant-floor event that caused this change was accepted, when there
/// was one. Carried, never re-stamped: it is the near end of a leg whose far end
/// is in another context, and only the Application layer that publishes onward
/// can hand it over (#2173).
/// </param>
public sealed record VariableValueChangedDomainEvent(
    VariableIdentifier Variable,
    FabIdentifier Fab,
    VariableName Name,
    VariableType Type,
    VariableValue Value,
    DateTimeOffset ChangedAt,
    OperatorIdentifier ChangedBy,
    BooleanLabels? BooleanLabels,
    Option<DateTimeOffset> RootIngestedAt) : IDomainEvent;
