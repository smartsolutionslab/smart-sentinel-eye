using System.Globalization;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

/// <summary>
/// Notification records on <see cref="ILayoutLifecycleBroadcaster"/>
/// are wire shapes — only their constructors and getters matter. These
/// tests pin the property contract so the Infrastructure SignalR
/// adapter cannot silently drift away from the Domain contract.
/// </summary>
public class LifecycleNotificationTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void LayoutRevisionPublishedNotification_exposes_every_field()
    {
        LayoutIdentifier layout = LayoutIdentifier.New();

        LayoutRevisionPublishedNotification notification = new(
            FabIdentifier.From("dresden"), layout, LayoutRevisionNumber.One,
            LayoutName.From("Line-1"), FixedMoment);

        // Singular, unlike the overlay frames below: a layout belongs to one
        // fab, and this is the group the frame is addressed to (FR-008).
        notification.Fab.Value.ShouldBe("dresden");
        notification.Layout.ShouldBe(layout);
        notification.RevisionNumber.ShouldBe(LayoutRevisionNumber.One);
        notification.Name.Value.ShouldBe("Line-1");
        notification.PublishedAt.ShouldBe(FixedMoment);
    }

    [Fact]
    public void LayoutRevisionArchivedNotification_exposes_every_field()
    {
        LayoutIdentifier layout = LayoutIdentifier.New();

        LayoutRevisionArchivedNotification notification = new(
            FabIdentifier.From("dresden"), layout, LayoutRevisionNumber.One, FixedMoment);

        notification.Fab.Value.ShouldBe("dresden");
        notification.Layout.ShouldBe(layout);
        notification.RevisionNumber.ShouldBe(LayoutRevisionNumber.One);
        notification.ArchivedAt.ShouldBe(FixedMoment);
    }

    [Fact]
    public void OverlayLifecyclePublishedNotification_exposes_every_field()
    {
        Guid overlay = Guid.CreateVersion7();

        OverlayLifecyclePublishedNotification notification = new(
            [FabIdentifier.From("munich"), FabIdentifier.From("dresden")],
            overlay, 1, "Line-1 Title",
            [new OverlayLifecycleElement("Text", "#FFFFFFD9", 0.5m, 0.05m, 0.3m, 0.08m, "Production Line 1", 48)],
            FixedMoment);

        // Plural, unlike the layout frames above. An overlay has no fab of its
        // own (ADR-0115); the set is who *references* it, so two plants
        // sharing a template are both told (FR-010).
        notification.Fabs.Select(fab => fab.Value).ShouldBe(["munich", "dresden"]);
        notification.Overlay.ShouldBe(overlay);
        notification.RevisionNumber.ShouldBe(1);
        notification.Name.ShouldBe("Line-1 Title");
        OverlayLifecycleElement element = notification.Elements.ShouldHaveSingleItem();
        element.Kind.ShouldBe("Text");
        element.Text.ShouldBe("Production Line 1");
        element.NormalizedX.ShouldBe(0.5m);
        element.NormalizedY.ShouldBe(0.05m);
        element.NormalizedWidth.ShouldBe(0.3m);
        element.NormalizedHeight.ShouldBe(0.08m);
        element.FontSizePx.ShouldBe(48);
        notification.PublishedAt.ShouldBe(FixedMoment);
    }

    /// <summary>
    /// Spec 150 (#2345): a revision carries an ordered set of 1..8 elements,
    /// not one flattened label. Spec 300 (#2349, ADR-0165) widened each
    /// element to carry a kind and colour.
    /// </summary>
    [Fact]
    public void OverlayLifecyclePublishedNotification_carries_every_element_in_order()
    {
        Guid overlay = Guid.CreateVersion7();

        OverlayLifecyclePublishedNotification notification = new(
            [FabIdentifier.From("munich")],
            overlay, 1, "Line-1 Title",
            [
                new OverlayLifecycleElement("Text", "#FFFFFFD9", 0.1m, 0.1m, 0.2m, 0.2m, "First", 16),
                new OverlayLifecycleElement("Text", "#FFFFFFD9", 0.2m, 0.2m, 0.2m, 0.2m, "Second", 20),
            ],
            FixedMoment);

        notification.Elements.Count.ShouldBe(2);
        notification.Elements[0].Text.ShouldBe("First");
        notification.Elements[1].Text.ShouldBe("Second");
    }

    /// <summary>
    /// Spec 300 (#2349), ADR-0165: a non-text element carries null text and
    /// font size.
    /// </summary>
    [Fact]
    public void OverlayLifecyclePublishedNotification_carries_a_non_text_element_with_null_text_and_font_size()
    {
        Guid overlay = Guid.CreateVersion7();

        OverlayLifecyclePublishedNotification notification = new(
            [FabIdentifier.From("munich")],
            overlay, 1, "Line-1 Title",
            [new OverlayLifecycleElement("Box", "#D32F2F00", 0.1m, 0.2m, 0.4m, 0.5m, null, null)],
            FixedMoment);

        OverlayLifecycleElement element = notification.Elements.ShouldHaveSingleItem();
        element.Kind.ShouldBe("Box");
        element.Text.ShouldBeNull();
        element.FontSizePx.ShouldBeNull();
    }

    /// <summary>
    /// Spec 150 (#2345): the resolved-text push carries every label's
    /// resolved text under one version bump, not a scalar.
    /// </summary>
    [Fact]
    public void ResolvedOverlayTextChangedNotification_carries_every_resolved_text()
    {
        Guid overlay = Guid.CreateVersion7();

        ResolvedOverlayTextChangedNotification notification = new(
            overlay, ["First", "Second"], 7, "munich");

        notification.Overlay.ShouldBe(overlay);
        notification.ResolvedTexts.Count.ShouldBe(2);
        notification.ResolvedTexts[0].ShouldBe("First");
        notification.ResolvedTexts[1].ShouldBe("Second");
        notification.Version.ShouldBe(7);
        notification.Fab.ShouldBe("munich");
    }

    [Fact]
    public void OverlayLifecycleArchivedNotification_exposes_every_field()
    {
        Guid overlay = Guid.CreateVersion7();

        // An empty set is legitimate and load-bearing: an overlay no published
        // layout references reaches nobody (FR-011).
        OverlayLifecycleArchivedNotification notification = new([], overlay, 2, FixedMoment);

        notification.Fabs.ShouldBeEmpty();
        notification.Overlay.ShouldBe(overlay);
        notification.RevisionNumber.ShouldBe(2);
        notification.ArchivedAt.ShouldBe(FixedMoment);
    }
}
