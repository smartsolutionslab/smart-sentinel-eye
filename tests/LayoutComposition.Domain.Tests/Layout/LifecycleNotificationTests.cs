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
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());

        LayoutRevisionPublishedNotification notification = new(
            layout, LayoutRevisionNumber.One, LayoutName.From("Line-1"), camera, FixedMoment);

        notification.Layout.ShouldBe(layout);
        notification.RevisionNumber.ShouldBe(LayoutRevisionNumber.One);
        notification.Name.Value.ShouldBe("Line-1");
        notification.Camera.ShouldBe(camera);
        notification.PublishedAt.ShouldBe(FixedMoment);
    }

    [Fact]
    public void LayoutRevisionArchivedNotification_exposes_every_field()
    {
        LayoutIdentifier layout = LayoutIdentifier.New();

        LayoutRevisionArchivedNotification notification = new(
            layout, LayoutRevisionNumber.One, FixedMoment);

        notification.Layout.ShouldBe(layout);
        notification.RevisionNumber.ShouldBe(LayoutRevisionNumber.One);
        notification.ArchivedAt.ShouldBe(FixedMoment);
    }

    [Fact]
    public void OverlayLifecyclePublishedNotification_exposes_every_field()
    {
        Guid overlay = Guid.CreateVersion7();

        OverlayLifecyclePublishedNotification notification = new(
            overlay, 1, "Line-1 Title",
            "Production Line 1", 0.5m, 0.05m, 0.3m, 0.08m, 48,
            FixedMoment);

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

        OverlayLifecycleArchivedNotification notification = new(overlay, 2, FixedMoment);

        notification.Overlay.ShouldBe(overlay);
        notification.RevisionNumber.ShouldBe(2);
        notification.ArchivedAt.ShouldBe(FixedMoment);
    }
}
