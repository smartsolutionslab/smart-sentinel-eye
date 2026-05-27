using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;

/// <summary>
/// Fluent builder for Variable aggregates in tests (ADR-0054). Sensible
/// defaults; .With...() overrides per scenario.
/// </summary>
public sealed class VariableBuilder
{
    private VariableName _name = VariableName.From("oeeLine1");
    private VariableType _type = VariableType.Number;
    private VariableValue? _initialValue;
    private BooleanLabels? _booleanLabels;
    private OperatorIdentifier _definedBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private IClock _clock = new TestClock(
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture));

    public VariableBuilder Named(string name)
    {
        _name = VariableName.From(name);
        return this;
    }

    public VariableBuilder OfType(VariableType type)
    {
        _type = type;
        return this;
    }

    public VariableBuilder WithInitialValue(VariableValue value)
    {
        _initialValue = value;
        return this;
    }

    public VariableBuilder WithBooleanLabels(BooleanLabels labels)
    {
        _booleanLabels = labels;
        return this;
    }

    public VariableBuilder DefinedBy(OperatorIdentifier definedBy)
    {
        _definedBy = definedBy;
        return this;
    }

    public VariableBuilder At(DateTimeOffset moment)
    {
        _clock = new TestClock(moment);
        return this;
    }

    public Domain.Variable.Variable Build() =>
        Domain.Variable.Variable.Define(
            _name, _type, _initialValue, _booleanLabels, _definedBy, _clock);

    public IClock Clock => _clock;

    public OperatorIdentifier Operator => _definedBy;

    public sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
