using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Domain.Variable.Events;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable;

public class VariableTests
{
    [Fact]
    public void Define_with_no_initial_value_starts_Unset_in_Defined_state()
    {
        Domain.Variable.Variable v = new VariableBuilder()
            .Named("oeeLine1").OfType(VariableType.Number).Build();

        v.Name.Value.ShouldBe("oeeLine1");
        v.Type.ShouldBe(VariableType.Number);
        v.Value.ShouldBeOfType<VariableValue.Unset>();
        v.State.ShouldBe(VariableState.Defined);
        v.BooleanLabels.ShouldBeNull();
    }

    [Fact]
    public void Define_raises_a_VariableDefinedDomainEvent()
    {
        Domain.Variable.Variable v = new VariableBuilder().Build();
        v.PendingEvents.OfType<VariableDefinedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Define_with_an_initial_value_carries_it()
    {
        Domain.Variable.Variable v = new VariableBuilder()
            .OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(82.4))
            .Build();
        v.Value.ShouldBeOfType<VariableValue.NumberValue>().Value.ShouldBe(82.4);
    }

    [Fact]
    public void Define_Boolean_without_BooleanLabels_throws()
    {
        Action act = () => new VariableBuilder()
            .OfType(VariableType.Boolean)
            .Build();
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Define_non_Boolean_with_BooleanLabels_throws()
    {
        Action act = () => new VariableBuilder()
            .OfType(VariableType.String)
            .WithBooleanLabels(BooleanLabels.Default)
            .Build();
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Define_with_a_mismatched_initial_value_throws()
    {
        Action act = () => new VariableBuilder()
            .OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.StringValue("nope"))
            .Build();
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void SetValue_replaces_the_value_and_raises_VariableValueChangedDomainEvent()
    {
        VariableBuilder b = new VariableBuilder().OfType(VariableType.Number);
        Domain.Variable.Variable v = b.Build();
        v.ClearPendingEvents();

        v.SetValue(new VariableValue.NumberValue(99.0), b.Operator, b.Clock);

        v.Value.ShouldBeOfType<VariableValue.NumberValue>().Value.ShouldBe(99.0);
        VariableValueChangedDomainEvent evt =
            v.PendingEvents.OfType<VariableValueChangedDomainEvent>().ShouldHaveSingleItem();
        evt.Value.ShouldBeOfType<VariableValue.NumberValue>().Value.ShouldBe(99.0);
    }

    [Fact]
    public void SetValue_with_mismatched_type_throws()
    {
        VariableBuilder b = new VariableBuilder().OfType(VariableType.Number);
        Domain.Variable.Variable v = b.Build();

        Action act = () => v.SetValue(new VariableValue.StringValue("nope"), b.Operator, b.Clock);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void SetValue_on_archived_throws()
    {
        VariableBuilder b = new VariableBuilder().OfType(VariableType.Number);
        Domain.Variable.Variable v = b.Build();
        v.Archive(b.Operator, b.Clock);

        Action act = () => v.SetValue(new VariableValue.NumberValue(1.0), b.Operator, b.Clock);
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Archive_transitions_state_and_clears_value()
    {
        VariableBuilder b = new VariableBuilder()
            .OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(82.4));
        Domain.Variable.Variable v = b.Build();
        v.ClearPendingEvents();

        v.Archive(b.Operator, b.Clock);

        v.State.ShouldBe(VariableState.Archived);
        v.Value.ShouldBeOfType<VariableValue.Unset>();
        v.PendingEvents.OfType<VariableArchivedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Archive_is_idempotent()
    {
        VariableBuilder b = new VariableBuilder();
        Domain.Variable.Variable v = b.Build();
        v.Archive(b.Operator, b.Clock);
        v.ClearPendingEvents();

        v.Archive(b.Operator, b.Clock);

        v.PendingEvents.ShouldBeEmpty();
    }
}
