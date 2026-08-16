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
            "Production Line 1", 0.5m, 0.05m, 0.3m, 0.08m, 48,
            FixedMoment);

        // Plural, unlike the layout frames above. An overlay has no fab of its
        // own (ADR-0115); the set is who *references* it, so two plants
        // sharing a template are both told (FR-010).
        notification.Fabs.Select(fab => fab.Value).ShouldBe(["munich", "dresden"]);
        notification.Overlay.ShouldBe(overlay);
        notification.RevisionNumber.ShouldBe(1);
        notification.Name.ShouldBe("Line-1 Title");
        notification.Text.ShouldBe("Production Line 1");
        notification.NormalizedX.ShouldBe(0.5m);
        notification.NormalizedY.ShouldBe(0.05m);
        notification.NormalizedWidth.ShouldBe(0.3m);
        notification.NormalizedHeight.ShouldBe(0.08m);
        notification.FontSizePx.ShouldBe(48);
        notification.PublishedAt.ShouldBe(FixedMoment);
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
