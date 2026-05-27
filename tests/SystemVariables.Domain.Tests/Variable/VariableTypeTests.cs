using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable;

public class VariableTypeTests
{
    [Fact]
    public void Three_canonical_singletons_with_expected_wire_strings()
    {
        VariableType.String.Value.ShouldBe("String");
        VariableType.Number.Value.ShouldBe("Number");
        VariableType.Boolean.Value.ShouldBe("Boolean");
    }

    [Theory]
    [InlineData("String")]
    [InlineData("Number")]
    [InlineData("Boolean")]
    public void From_a_valid_wire_string_round_trips(string wire)
    {
        VariableType.From(wire).Value.ShouldBe(wire);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("number")]
    [InlineData("")]
    [InlineData("Decimal")]
    public void From_an_invalid_wire_string_throws(string wire)
    {
        Action act = () => VariableType.From(wire);
        act.ShouldThrow<ArgumentException>();
    }
}
