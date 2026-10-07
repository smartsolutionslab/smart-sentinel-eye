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
    private FabIdentifier fab = FabIdentifier.From("munich");
    private VariableName name = VariableName.From("oeeLine1");
    private VariableType type = VariableType.Number;
    private VariableValue? initialValue;
    private BooleanLabels? booleanLabels;
    private OperatorIdentifier definedBy = OperatorIdentifier.From(Guid.CreateVersion7());
    private IClock clock = new TestClock(
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture));

    public VariableBuilder WithFab(string fab)
    {
        this.fab = FabIdentifier.From(fab);
        return this;
    }

    public VariableBuilder Named(string name)
    {
        this.name = VariableName.From(name);
        return this;
    }

    public VariableBuilder OfType(VariableType type)
    {
        this.type = type;
        return this;
    }

    public VariableBuilder WithInitialValue(VariableValue value)
    {
        initialValue = value;
        return this;
    }

    public VariableBuilder WithBooleanLabels(BooleanLabels labels)
    {
        booleanLabels = labels;
        return this;
    }

    public VariableBuilder DefinedBy(OperatorIdentifier definedBy)
    {
        this.definedBy = definedBy;
        return this;
    }

    public VariableBuilder At(DateTimeOffset moment)
    {
        clock = new TestClock(moment);
        return this;
    }

    public Domain.Variable.Variable Build() =>
        Domain.Variable.Variable.Define(
            fab, name, type, initialValue, booleanLabels, definedBy, clock);

    public IClock Clock => clock;

    public OperatorIdentifier Operator => definedBy;

    public sealed class TestClock(DateTimeOffset moment) : IClock
    {
        public DateTimeOffset UtcNow { get; } = moment;
    }
}
