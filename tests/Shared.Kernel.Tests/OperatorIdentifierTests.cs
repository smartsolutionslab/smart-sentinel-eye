using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Shared.Kernel.Tests;

public class OperatorIdentifierTests
{
    [Fact]
    public void From_a_non_empty_Guid_returns_a_wrapping_value()
    {
        Guid raw = Guid.CreateVersion7();

        OperatorIdentifier identifier = OperatorIdentifier.From(raw);

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
        Guid raw = Guid.CreateVersion7();
        OperatorIdentifier first = OperatorIdentifier.From(raw);
        OperatorIdentifier second = OperatorIdentifier.From(raw);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void ToString_returns_the_Guid_string_form()
    {
        Guid raw = Guid.CreateVersion7();
        OperatorIdentifier identifier = OperatorIdentifier.From(raw);

        identifier.ToString().ShouldBe(raw.ToString());
    }
}
