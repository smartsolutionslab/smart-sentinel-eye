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
            [new OverlayLifecycleLabel("Production Line 1", 0.5m, 0.05m, 0.3m, 0.08m, 48)],
            FixedMoment);

        // Plural, unlike the layout frames above. An overlay has no fab of its
        // own (ADR-0115); the set is who *references* it, so two plants
        // sharing a template are both told (FR-010).
        notification.Fabs.Select(fab => fab.Value).ShouldBe(["munich", "dresden"]);
        notification.Overlay.ShouldBe(overlay);
        notification.RevisionNumber.ShouldBe(1);
        notification.Name.ShouldBe("Line-1 Title");
        OverlayLifecycleLabel label = notification.Labels.ShouldHaveSingleItem();
        label.Text.ShouldBe("Production Line 1");
        label.NormalizedX.ShouldBe(0.5m);
        label.NormalizedY.ShouldBe(0.05m);
        label.NormalizedWidth.ShouldBe(0.3m);
        label.NormalizedHeight.ShouldBe(0.08m);
        label.FontSizePx.ShouldBe(48);
        notification.PublishedAt.ShouldBe(FixedMoment);
    }

    /// <summary>
    /// Spec 150 (#2345): a revision carries an ordered set of 1..8 labels,
    /// not one flattened label.
    /// </summary>
    [Fact]
    public void OverlayLifecyclePublishedNotification_carries_every_label_in_order()
    {
        Guid overlay = Guid.CreateVersion7();

        OverlayLifecyclePublishedNotification notification = new(
            [FabIdentifier.From("munich")],
            overlay, 1, "Line-1 Title",
            [
                new OverlayLifecycleLabel("First", 0.1m, 0.1m, 0.2m, 0.2m, 16),
                new OverlayLifecycleLabel("Second", 0.2m, 0.2m, 0.2m, 0.2m, 20),
            ],
            FixedMoment);

        notification.Labels.Count.ShouldBe(2);
        notification.Labels[0].Text.ShouldBe("First");
        notification.Labels[1].Text.ShouldBe("Second");
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
