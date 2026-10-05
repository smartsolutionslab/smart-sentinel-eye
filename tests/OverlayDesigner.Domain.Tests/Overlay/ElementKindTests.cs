using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// Spec 300 (#2349), ADR-0165, T005: a closed-set value object on
/// <see cref="OverlayRevisionState"/>'s pattern — three named singletons
/// and a <c>From</c> that throws on anything else.
/// </summary>
public class ElementKindTests
{
    [Fact]
    public void Singletons_carry_their_canonical_values()
    {
        ElementKind.Text.Value.ShouldBe("Text");
        ElementKind.Box.Value.ShouldBe("Box");
        ElementKind.Ellipse.Value.ShouldBe("Ellipse");
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("Box")]
    [InlineData("Ellipse")]
    public void From_round_trips_canonical_strings(string raw)
    {
        ElementKind parsed = ElementKind.From(raw);
        parsed.Value.ShouldBe(raw);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("Shape")]
    [InlineData("Circle")]
    [InlineData("")]
    public void From_rejects_an_unknown_value(string raw)
    {
        Should.Throw<ArgumentException>(() => ElementKind.From(raw));
    }

    [Fact]
    public void Same_canonical_value_yields_the_singleton_instance()
    {
        ElementKind.From("Text").ShouldBe(ElementKind.Text);
        ElementKind.From("Box").ShouldBe(ElementKind.Box);
        ElementKind.From("Ellipse").ShouldBe(ElementKind.Ellipse);
    }
}
