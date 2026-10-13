namespace SmartSentinelEye.Shared.Kernel.Tests;

public class OperatorIdentifierTests
{
    [Fact]
    public void From_a_non_empty_Guid_returns_a_wrapping_value()
    {
        var raw = Guid.CreateVersion7();

        var identifier = OperatorIdentifier.From(raw);

        identifier.Value.ShouldBe(raw);
    }

    [Fact]
    public void From_Guid_Empty_throws()
    {
        Action act = () => OperatorIdentifier.From(Guid.Empty);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Two_identifiers_with_the_same_Guid_are_equal()
    {
        var raw = Guid.CreateVersion7();
        var first = OperatorIdentifier.From(raw);
        var second = OperatorIdentifier.From(raw);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void ToString_returns_the_Guid_string_form()
    {
        var raw = Guid.CreateVersion7();
        var identifier = OperatorIdentifier.From(raw);

        identifier.ToString().ShouldBe(raw.ToString());
    }
}
