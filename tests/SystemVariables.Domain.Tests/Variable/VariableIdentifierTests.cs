using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable;

public class VariableIdentifierTests
{
    [Fact]
    public void New_returns_a_non_empty_Guid_v7()
    {
        var identifier = VariableIdentifier.New();
        identifier.Value.ShouldNotBe(Guid.Empty);
        // Guid v7 sets the version nibble to 0x7.
        ((identifier.Value.ToByteArray()[7] & 0xF0) >> 4).ShouldBe(0x7);
    }

    [Fact]
    public void From_a_non_empty_Guid_wraps_the_value()
    {
        var raw = Guid.CreateVersion7();
        var identifier = VariableIdentifier.From(raw);
        identifier.Value.ShouldBe(raw);
    }

    [Fact]
    public void From_Guid_Empty_throws_ArgumentException()
    {
        Action act = () => VariableIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void ToString_returns_the_underlying_Guid_string()
    {
        var raw = Guid.CreateVersion7();
        var identifier = VariableIdentifier.From(raw);
        identifier.ToString().ShouldBe(raw.ToString());
    }
}
