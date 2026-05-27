using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

public class OverlayRevisionStateTests
{
    [Fact]
    public void Singletons_carry_their_canonical_values()
    {
        OverlayRevisionState.Draft.Value.ShouldBe("Draft");
        OverlayRevisionState.Published.Value.ShouldBe("Published");
        OverlayRevisionState.Archived.Value.ShouldBe("Archived");
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Published")]
    [InlineData("Archived")]
    public void From_round_trips_canonical_strings(string raw)
    {
        OverlayRevisionState parsed = OverlayRevisionState.From(raw);
        parsed.Value.ShouldBe(raw);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void From_rejects_an_unknown_value(string raw)
    {
        Should.Throw<ArgumentException>(() => OverlayRevisionState.From(raw));
    }

    [Fact]
    public void Same_canonical_value_yields_the_singleton_instance()
    {
        OverlayRevisionState.From("Draft").ShouldBe(OverlayRevisionState.Draft);
        OverlayRevisionState.From("Published").ShouldBe(OverlayRevisionState.Published);
        OverlayRevisionState.From("Archived").ShouldBe(OverlayRevisionState.Archived);
    }
}
