using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable.Events;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// Aggregate root for a system variable (spec 005). Plain CRUD per
/// ADR-0009 — no event sourcing, no revision chain. State machine is
/// two-state: <c>Defined</c> → <c>Archived</c>; the value can flip
/// between <c>Unset</c> and a typed case while the variable stays
/// <c>Defined</c>.
///
/// <para>
/// Name uniqueness across non-Archived variables is enforced by the
/// Application layer's repository lookup (FR-006 sibling rule;
/// archived names are released for re-use).
/// </para>
/// </summary>
public sealed class Variable : AggregateRoot<VariableIdentifier>
{
    public VariableName Name { get; private set; } = null!;

    public VariableType Type { get; private set; } = null!;

    public VariableValue Value { get; private set; } = null!;

    public VariableState State { get; private set; } = null!;

    /// <summary>
    /// Non-null when <see cref="Type"/> is <c>Boolean</c>; null otherwise.
    /// Carries the truthy/falsy render strings used by the resolver.
    /// </summary>
    public BooleanLabels? BooleanLabels { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public OperatorIdentifier CreatedBy { get; private set; }

    private Variable() { }

    /// <summary>
    /// Mints a new variable in <c>Defined</c> state. Raises a
    /// <see cref="VariableDefinedDomainEvent"/>.
    /// </summary>
    public static Variable Define(
        VariableName name,
        VariableType type,
        VariableValue? initialValue,
        BooleanLabels? booleanLabels,
        OperatorIdentifier definedBy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(clock);

        if (type == VariableType.Boolean && booleanLabels is null)
        {
            throw new InvalidOperationException(
                "BooleanLabels are required when defining a Boolean variable.");
        }
        if (type != VariableType.Boolean && booleanLabels is not null)
        {
            throw new InvalidOperationException(
                "BooleanLabels can only be set on Boolean variables.");
        }

        VariableValue value = initialValue ?? VariableValue.Unset.Instance;
        EnsureValueMatchesType(type, value);

        DateTimeOffset now = clock.UtcNow;
        Variable variable = new()
        {
            Id = VariableIdentifier.New(),
            Name = name,
            Type = type,
            Value = value,
            State = VariableState.Defined,
            BooleanLabels = booleanLabels,
            CreatedAt = now,
            CreatedBy = definedBy,
        };
        variable.Raise(new VariableDefinedDomainEvent(variable.Id, name, type, now, definedBy));
        return variable;
    }

    /// <summary>
    /// Replaces the current value. Must match the variable's declared
    /// type. Only callable while <c>Defined</c>.
    /// </summary>
    public void SetValue(VariableValue value, OperatorIdentifier changedBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(clock);
        if (State != VariableState.Defined)
        {
            throw new InvalidOperationException(
                $"Variable {Id} is {State}; only Defined variables accept value updates.");
        }
        EnsureValueMatchesType(Type, value);
        Value = value;
        Raise(new VariableValueChangedDomainEvent(
            Id, Name, Type, value, clock.UtcNow, changedBy, BooleanLabels));
    }

    /// <summary>
    /// Archives the variable. Idempotent on Archived (no event raised).
    /// </summary>
    public void Archive(OperatorIdentifier archivedBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (State == VariableState.Archived) return;
        State = VariableState.Archived;
        Value = VariableValue.Unset.Instance;
        Raise(new VariableArchivedDomainEvent(Id, Name, clock.UtcNow, archivedBy));
    }

    private static void EnsureValueMatchesType(VariableType type, VariableValue value)
    {
        if (value is VariableValue.Unset) return;

        bool matches = (type, value) switch
        {
            _ when type == VariableType.String && value is VariableValue.StringValue => true,
            _ when type == VariableType.Number && value is VariableValue.NumberValue => true,
            _ when type == VariableType.Boolean && value is VariableValue.BooleanValue => true,
            _ => false,
        };
        if (!matches)
        {
            throw new ArgumentException(
                $"Value case {value.GetType().Name} does not match type {type}.",
                nameof(value));
        }
    }
}
