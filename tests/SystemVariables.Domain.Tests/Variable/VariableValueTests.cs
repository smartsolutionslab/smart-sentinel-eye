using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable;

public class VariableValueTests
{
    [Fact]
    public void Unset_is_a_singleton_record()
    {
        VariableValue.Unset.Instance.ShouldBe(VariableValue.Unset.Instance);
    }

    [Fact]
    public void From_String_returns_a_StringValue()
    {
        VariableValue value = VariableValue.From(VariableType.String, "hello");
        value.ShouldBeOfType<VariableValue.StringValue>().Value.ShouldBe("hello");
    }

    [Theory]
    [InlineData("82.4", 82.4)]
    [InlineData("-1.5", -1.5)]
    [InlineData("0", 0.0)]
    [InlineData("1000000", 1_000_000.0)]
    public void From_Number_parses_culture_invariantly(string wire, double expected)
    {
        VariableValue value = VariableValue.From(VariableType.Number, wire);
        value.ShouldBeOfType<VariableValue.NumberValue>().Value.ShouldBe(expected);
    }

    [Fact]
    public void From_Number_rejects_invalid_decimal()
    {
        Action act = () => VariableValue.From(VariableType.Number, "not-a-number");
        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void From_Boolean_parses_lowercase_literals(string wire, bool expected)
    {
        VariableValue value = VariableValue.From(VariableType.Boolean, wire);
        value.ShouldBeOfType<VariableValue.BooleanValue>().Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("FALSE")]
    [InlineData("yes")]
    public void From_Boolean_rejects_non_canonical_literals(string wire)
    {
        Action act = () => VariableValue.From(VariableType.Boolean, wire);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void ToWireString_round_trips_String()
    {
        VariableValue v = new VariableValue.StringValue("hello");
        v.ToWireString().ShouldBe("hello");
    }

    [Fact]
    public void ToWireString_round_trips_Number_invariantly()
    {
        VariableValue v = new VariableValue.NumberValue(82.4);
        // Round trip via From + ToWireString preserves the value.
        VariableValue back = VariableValue.From(VariableType.Number, v.ToWireString());
        back.ShouldBeOfType<VariableValue.NumberValue>().Value.ShouldBe(82.4);
    }

    [Fact]
    public void ToWireString_round_trips_Boolean()
    {
        new VariableValue.BooleanValue(true).ToWireString().ShouldBe("true");
        new VariableValue.BooleanValue(false).ToWireString().ShouldBe("false");
    }

    [Fact]
    public void Render_String_returns_the_raw_text()
    {
        new VariableValue.StringValue("Production Line 1")
            .Render(BooleanLabels.Default).ShouldBe("Production Line 1");
    }

    [Fact]
    public void Render_Number_is_culture_invariant()
    {
        new VariableValue.NumberValue(82.4)
            .Render(BooleanLabels.Default).ShouldBe("82.4");
    }

    [Fact]
    public void Render_Boolean_uses_the_supplied_labels()
    {
        BooleanLabels labels = BooleanLabels.From("Running", "Stopped");
        new VariableValue.BooleanValue(true).Render(labels).ShouldBe("Running");
        new VariableValue.BooleanValue(false).Render(labels).ShouldBe("Stopped");
    }

    [Fact]
    public void Render_throws_on_Unset()
    {
        Action act = () => VariableValue.Unset.Instance.Render(BooleanLabels.Default);
        act.ShouldThrow<InvalidOperationException>();
    }
}
