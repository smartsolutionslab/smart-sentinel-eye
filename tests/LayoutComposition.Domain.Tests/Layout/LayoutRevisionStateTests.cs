using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class LayoutRevisionStateTests
{
    [Fact]
    public void Singletons_carry_their_canonical_values()
    {
        LayoutRevisionState.Draft.Value.ShouldBe("Draft");
        LayoutRevisionState.Published.Value.ShouldBe("Published");
        LayoutRevisionState.Archived.Value.ShouldBe("Archived");
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Published")]
    [InlineData("Archived")]
    public void From_round_trips_canonical_strings(string raw)
    {
        LayoutRevisionState parsed = LayoutRevisionState.From(raw);
        parsed.Value.ShouldBe(raw);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void From_rejects_an_unknown_value(string raw)
    {
        Action act = () => LayoutRevisionState.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Same_canonical_value_yields_the_singleton_instance()
    {
        LayoutRevisionState.From("Draft").ShouldBe(LayoutRevisionState.Draft);
        LayoutRevisionState.From("Published").ShouldBe(LayoutRevisionState.Published);
        LayoutRevisionState.From("Archived").ShouldBe(LayoutRevisionState.Archived);
    }
}
