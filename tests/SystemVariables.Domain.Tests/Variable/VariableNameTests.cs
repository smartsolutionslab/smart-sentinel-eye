using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable;

public class VariableNameTests
{
    [Theory]
    [InlineData("oeeLine1")]
    [InlineData("currentShift")]
    [InlineData("a")]
    [InlineData("A1_b2_C3")]
    public void Accepts_valid_grammar(string value)
    {
        VariableName.From(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1leading")]      // starts with digit
    [InlineData("_underscore")]    // starts with underscore
    [InlineData("has space")]
    [InlineData("has-dash")]
    [InlineData("has.dot")]
    public void Rejects_invalid_grammar(string value)
    {
        Action act = () => VariableName.From(value);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Rejects_names_longer_than_64_chars()
    {
        string name = "a" + new string('b', 64);
        Action act = () => VariableName.From(name);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Accepts_names_exactly_64_chars()
    {
        string name = "a" + new string('b', 63);
        VariableName.From(name).Value.ShouldBe(name);
    }

    [Fact]
    public void Is_case_sensitive()
    {
        VariableName a = VariableName.From("oeeLine1");
        VariableName b = VariableName.From("oeeline1");
        a.ShouldNotBe(b);
    }
}
