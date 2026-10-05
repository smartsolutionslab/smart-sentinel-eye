using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// Spec 300 (#2349), ADR-0165, T006: the three factories are the only
/// public constructors and are where the presence rule (a <c>Text</c>
/// component iff <c>Kind == Text</c>) and the text-legibility invariant are
/// enforced. <see cref="OverlayElementSetTests"/> covers the set-level
/// <c>TooMany</c> rule across kinds.
/// </summary>
public class OverlayElementTests
{
    private static NormalizedPosition SomePosition() => NormalizedPosition.From(0.1m, 0.2m);

    private static NormalizedSize SomeSize() => NormalizedSize.From(0.4m, 0.5m);

    // --- Presence table: Text carries a TextContent, Box/Ellipse never do ---

    [Fact]
    public void TextElement_carries_its_text_and_font_size()
    {
        OverlayElement element = OverlayElement.TextElement(
            "Zone A", 24, SomePosition(), SomeSize(), OverlayColor.From("#d32f2f"));

        element.Kind.ShouldBe(ElementKind.Text);
        element.Text.ShouldNotBeNull();
        element.Text.Value.ShouldBe("Zone A");
        element.Text.FontSizePx.ShouldBe(24);
    }

    [Fact]
    public void Box_has_no_text_component()
    {
        OverlayElement element = OverlayElement.Box(SomePosition(), SomeSize(), OverlayColor.From("#d32f2f"));

        element.Kind.ShouldBe(ElementKind.Box);
        element.Text.ShouldBeNull();
    }

    [Fact]
    public void Ellipse_has_no_text_component()
    {
        OverlayElement element = OverlayElement.Ellipse(SomePosition(), SomeSize(), OverlayColor.From("#FFA000CC"));

        element.Kind.ShouldBe(ElementKind.Ellipse);
        element.Text.ShouldBeNull();
    }

    [Fact]
    public void Box_and_Ellipse_carry_the_shared_geometry()
    {
        NormalizedPosition position = SomePosition();
        NormalizedSize size = SomeSize();

        OverlayElement box = OverlayElement.Box(position, size, OverlayColor.From("#d32f2f"));
        OverlayElement ellipse = OverlayElement.Ellipse(position, size, OverlayColor.From("#FFA000CC"));

        box.Position.ShouldBe(position);
        box.Size.ShouldBe(size);
        ellipse.Position.ShouldBe(position);
        ellipse.Size.ShouldBe(size);
    }

    // --- Text legibility is enforced only by TextElement ---

    [Fact]
    public void TextElement_refuses_a_colour_one_step_below_its_legibility_floor()
    {
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            OverlayElement.TextElement(
                "Zone A", 24, SomePosition(), SomeSize(), OverlayColor.From("#D32F2FF8")));

        exception.ParamName.ShouldBe("color");
    }

    [Fact]
    public void TextElement_refusal_message_carries_the_colours_minimum_alpha()
    {
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            OverlayElement.TextElement(
                "Zone A", 24, SomePosition(), SomeSize(), OverlayColor.From("#D32F2F80")));

        exception.Message.ShouldContain("F9");
    }

    [Fact]
    public void TextElement_accepts_a_colour_exactly_at_its_legibility_floor()
    {
        OverlayElement element = OverlayElement.TextElement(
            "Zone A", 24, SomePosition(), SomeSize(), OverlayColor.From("#D32F2FF9"));

        element.Color.Value.ShouldBe("#D32F2FF9");
    }

    [Fact]
    public void Box_accepts_a_fully_transparent_colour()
    {
        OverlayElement element = OverlayElement.Box(SomePosition(), SomeSize(), OverlayColor.From("#D32F2F00"));

        element.Color.Value.ShouldBe("#D32F2F00");
    }

    [Fact]
    public void Ellipse_accepts_a_translucent_colour()
    {
        OverlayElement element = OverlayElement.Ellipse(SomePosition(), SomeSize(), OverlayColor.From("#FFA00040"));

        element.Color.Value.ShouldBe("#FFA00040");
    }
}
