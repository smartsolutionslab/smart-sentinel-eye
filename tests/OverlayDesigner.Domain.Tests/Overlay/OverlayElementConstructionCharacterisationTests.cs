using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;
using DomainOverlay = SmartSentinelEye.OverlayDesigner.Domain.Overlay.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// Characterisation (spec 350, ADR-0144 phase 4a: behaviour-preserving, so
/// green on the unrefactored <see cref="OverlayElement"/> and green unmodified
/// after). Reaches only the public API: the three factories, and
/// <c>DeepClone</c>/<c>AtOrdinal</c> (both internal, no InternalsVisibleTo)
/// through the aggregate that calls them when it builds a revision.
///
/// <para>
/// Counterfactual this file was seen to catch: in <c>DeepClone</c>, passing
/// <c>Color</c> instead of <c>Color with { }</c> leaves a stored element
/// sharing its source's colour instance, and the not-same-instance assertions
/// fail.
/// </para>
///
/// <para>
/// Every expected value is a literal. Position, size, colour and text are all
/// different per element so a transposition between the parts fails.
/// </para>
/// </summary>
public class OverlayElementConstructionCharacterisationTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Editor =
        OperatorIdentifier.From(Guid.Parse("11111111-2222-7333-8444-555555555555"));

    [Fact]
    public void TextElement_keeps_the_instances_it_is_handed_and_the_text_it_is_given()
    {
        var position = NormalizedPosition.From(0.11m, 0.22m);
        var size = NormalizedSize.From(0.33m, 0.44m);
        var color = OverlayColor.From("#D32F2FFF");

        var element = OverlayElement.TextElement("  Zone A  ", 24, position, size, color);

        element.Kind.ShouldBe(ElementKind.Text);
        element.Position.ShouldBeSameAs(position);
        element.Size.ShouldBeSameAs(size);
        element.Color.ShouldBeSameAs(color);
        element.Text.ShouldNotBeNull();
        element.Text.Value.ShouldBe("Zone A");
        element.Text.FontSizePx.ShouldBe(24);
        element.Ordinal.Value.ShouldBe(0);
    }

    [Fact]
    public void Box_keeps_the_instances_it_is_handed_and_has_no_text()
    {
        var position = NormalizedPosition.From(0.55m, 0.66m);
        var size = NormalizedSize.From(0.77m, 0.88m);
        var color = OverlayColor.From("#00FF00AA");

        var element = OverlayElement.Box(position, size, color);

        element.Kind.ShouldBe(ElementKind.Box);
        element.Position.ShouldBeSameAs(position);
        element.Size.ShouldBeSameAs(size);
        element.Color.ShouldBeSameAs(color);
        element.Text.ShouldBeNull();
        element.Ordinal.Value.ShouldBe(0);
    }

    [Fact]
    public void Ellipse_keeps_the_instances_it_is_handed_and_has_no_text()
    {
        var position = NormalizedPosition.From(0.12m, 0.34m);
        var size = NormalizedSize.From(0.56m, 0.78m);
        var color = OverlayColor.From("#0000FF10");

        var element = OverlayElement.Ellipse(position, size, color);

        element.Kind.ShouldBe(ElementKind.Ellipse);
        element.Position.ShouldBeSameAs(position);
        element.Size.ShouldBeSameAs(size);
        element.Color.ShouldBeSameAs(color);
        element.Text.ShouldBeNull();
        element.Ordinal.Value.ShouldBe(0);
    }

    [Fact]
    public void TextElement_refuses_a_null_position_before_a_null_size_and_before_an_illegible_colour()
    {
        var thrown = Should.Throw<ArgumentNullException>(() => OverlayElement.TextElement(
            "Zone A",
            24,
            null!,
            null!,
            OverlayColor.From("#FFFFFF10")));

        thrown.ParamName.ShouldBe("position");
        thrown.Message.ShouldBe("Value cannot be null. (Parameter 'position')");
    }

    [Fact]
    public void TextElement_refuses_a_null_size_before_a_null_colour()
    {
        var thrown = Should.Throw<ArgumentNullException>(() => OverlayElement.TextElement(
            "Zone A",
            24,
            NormalizedPosition.From(0.11m, 0.22m),
            null!,
            null!));

        thrown.ParamName.ShouldBe("size");
        thrown.Message.ShouldBe("Value cannot be null. (Parameter 'size')");
    }

    [Fact]
    public void TextElement_refuses_a_null_colour_naming_it()
    {
        var thrown = Should.Throw<ArgumentNullException>(() => OverlayElement.TextElement(
            "Zone A",
            24,
            NormalizedPosition.From(0.11m, 0.22m),
            NormalizedSize.From(0.33m, 0.44m),
            null!));

        thrown.ParamName.ShouldBe("color");
        thrown.Message.ShouldBe("Value cannot be null. (Parameter 'color')");
    }

    [Fact]
    public void Box_refuses_a_null_position_before_a_null_size_and_a_null_size_before_a_null_colour()
    {
        var position = Should.Throw<ArgumentNullException>(() => OverlayElement.Box(null!, null!, null!));
        var size = Should.Throw<ArgumentNullException>(() => OverlayElement.Box(
            NormalizedPosition.From(0.55m, 0.66m),
            null!,
            null!));
        var color = Should.Throw<ArgumentNullException>(() => OverlayElement.Box(
            NormalizedPosition.From(0.55m, 0.66m),
            NormalizedSize.From(0.77m, 0.88m),
            null!));

        position.Message.ShouldBe("Value cannot be null. (Parameter 'position')");
        size.Message.ShouldBe("Value cannot be null. (Parameter 'size')");
        color.Message.ShouldBe("Value cannot be null. (Parameter 'color')");
    }

    [Fact]
    public void Ellipse_refuses_a_null_position_before_a_null_size_and_a_null_size_before_a_null_colour()
    {
        var position = Should.Throw<ArgumentNullException>(() => OverlayElement.Ellipse(null!, null!, null!));
        var size = Should.Throw<ArgumentNullException>(() => OverlayElement.Ellipse(
            NormalizedPosition.From(0.12m, 0.34m),
            null!,
            null!));
        var color = Should.Throw<ArgumentNullException>(() => OverlayElement.Ellipse(
            NormalizedPosition.From(0.12m, 0.34m),
            NormalizedSize.From(0.56m, 0.78m),
            null!));

        position.Message.ShouldBe("Value cannot be null. (Parameter 'position')");
        size.Message.ShouldBe("Value cannot be null. (Parameter 'size')");
        color.Message.ShouldBe("Value cannot be null. (Parameter 'color')");
    }

    [Fact]
    public void TextElement_refuses_an_illegible_colour_before_it_looks_at_the_text()
    {
        var thrown = Should.Throw<ArgumentException>(() => OverlayElement.TextElement(
            "   ",
            24,
            NormalizedPosition.From(0.11m, 0.22m),
            NormalizedSize.From(0.33m, 0.44m),
            OverlayColor.From("#FFFFFF10")));

        thrown.GetType().ShouldBe(typeof(ArgumentException));
        thrown.ParamName.ShouldBe("color");
        thrown.Message.ShouldBe(
            "A text surface coloured #FFFFFF needs alpha of at least 75 to stay readable over any video. (Parameter 'color')");
    }

    [Fact]
    public void TextElement_refuses_blank_text_once_the_colour_is_legible()
    {
        var thrown = Should.Throw<ArgumentException>(() => OverlayElement.TextElement(
            "   ",
            24,
            NormalizedPosition.From(0.11m, 0.22m),
            NormalizedSize.From(0.33m, 0.44m),
            OverlayColor.From("#D32F2FFF")));

        thrown.GetType().ShouldBe(typeof(ArgumentException));
        thrown.ParamName.ShouldBe("text");
        thrown.Message.ShouldBe("text must not be null or whitespace. (Parameter 'text')");
    }

    [Fact]
    public void A_revisions_elements_are_deep_copies_of_the_sources_with_dense_ordinals()
    {
        var sources = Sources();

        var overlay = DomainOverlay.CreateDraft(OverlayName.From("Line-1"), sources, Editor, new FixedClock());

        var stored = overlay.Revisions.Single().Elements;
        AssertDeepCopiesWithDenseOrdinals(stored, sources);
    }

    [Fact]
    public void A_branched_drafts_elements_are_deep_copies_of_the_published_revisions()
    {
        var sources = Sources();
        var overlay = DomainOverlay.CreateDraft(OverlayName.From("Line-2"), sources, Editor, new FixedClock());
        overlay.Publish(OverlayRevisionNumber.One, Editor, new FixedClock());
        var published = overlay.Revisions.Single().Elements;

        overlay.BranchDraft(Editor, new FixedClock());

        var branched = overlay.Revisions.Single(revision => revision.Number == OverlayRevisionNumber.From(2)).Elements;
        AssertDeepCopiesWithDenseOrdinals(branched, published);
        AssertDeepCopiesWithDenseOrdinals(branched, sources);
    }

    [Fact]
    public void An_edited_drafts_elements_are_deep_copies_of_the_replacements_with_dense_ordinals()
    {
        var overlay = DomainOverlay.CreateDraft(OverlayName.From("Line-3"), Sources(), Editor, new FixedClock());
        var replacements = Sources();

        overlay.EditDraft(OverlayRevisionNumber.One, replacements, new FixedClock());

        AssertDeepCopiesWithDenseOrdinals(overlay.Revisions.Single().Elements, replacements);
    }

    private static List<OverlayElement> Sources() =>
    [
        OverlayElement.TextElement(
            "Zone A",
            24,
            NormalizedPosition.From(0.11m, 0.22m),
            NormalizedSize.From(0.33m, 0.44m),
            OverlayColor.From("#D32F2FFF")),
        OverlayElement.Box(
            NormalizedPosition.From(0.55m, 0.66m),
            NormalizedSize.From(0.77m, 0.88m),
            OverlayColor.From("#00FF00AA")),
        OverlayElement.Ellipse(
            NormalizedPosition.From(0.12m, 0.34m),
            NormalizedSize.From(0.56m, 0.78m),
            OverlayColor.From("#0000FF10")),
    ];

    private static void AssertDeepCopiesWithDenseOrdinals(
        IReadOnlyList<OverlayElement> copies,
        IReadOnlyList<OverlayElement> sources)
    {
        copies.Count.ShouldBe(3);

        copies[0].Kind.ShouldBe(ElementKind.Text);
        copies[0].Position.ShouldBe(NormalizedPosition.From(0.11m, 0.22m));
        copies[0].Size.ShouldBe(NormalizedSize.From(0.33m, 0.44m));
        copies[0].Color.Value.ShouldBe("#D32F2FFF");
        copies[0].Text.ShouldNotBeNull();
        copies[0].Text!.Value.ShouldBe("Zone A");
        copies[0].Text!.FontSizePx.ShouldBe(24);
        copies[0].Ordinal.Value.ShouldBe(0);

        copies[1].Kind.ShouldBe(ElementKind.Box);
        copies[1].Position.ShouldBe(NormalizedPosition.From(0.55m, 0.66m));
        copies[1].Size.ShouldBe(NormalizedSize.From(0.77m, 0.88m));
        copies[1].Color.Value.ShouldBe("#00FF00AA");
        copies[1].Text.ShouldBeNull();
        copies[1].Ordinal.Value.ShouldBe(1);

        copies[2].Kind.ShouldBe(ElementKind.Ellipse);
        copies[2].Position.ShouldBe(NormalizedPosition.From(0.12m, 0.34m));
        copies[2].Size.ShouldBe(NormalizedSize.From(0.56m, 0.78m));
        copies[2].Color.Value.ShouldBe("#0000FF10");
        copies[2].Text.ShouldBeNull();
        copies[2].Ordinal.Value.ShouldBe(2);

        for (var index = 0; index < copies.Count; index++)
        {
            copies[index].ShouldNotBeSameAs(sources[index]);
            copies[index].Position.ShouldNotBeSameAs(sources[index].Position);
            copies[index].Size.ShouldNotBeSameAs(sources[index].Size);
            copies[index].Color.ShouldNotBeSameAs(sources[index].Color);
        }

        copies[0].Text.ShouldNotBeSameAs(sources[0].Text);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => FixedMoment;
    }
}
