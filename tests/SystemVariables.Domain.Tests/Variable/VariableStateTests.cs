using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable;

public class VariableStateTests
{
    [Fact]
    public void Two_canonical_singletons_with_expected_wire_strings()
    {
        VariableState.Defined.Value.ShouldBe("Defined");
        VariableState.Archived.Value.ShouldBe("Archived");
    }

    [Theory]
    [InlineData("Defined")]
    [InlineData("Archived")]
    public void From_a_valid_wire_string_round_trips(string wire)
    {
        VariableState.From(wire).Value.ShouldBe(wire);
    }

    [Fact]
    public void From_an_invalid_wire_string_throws()
    {
        Action act = () => VariableState.From("Bogus");
        act.ShouldThrow<ArgumentException>();
    }
}
