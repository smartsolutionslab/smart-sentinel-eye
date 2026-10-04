using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Tests.Layout.Builders;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

public class OverlayRevisionPublishedV2HandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Relays_the_overlay_publish_onto_the_broadcaster_with_every_field_mapped()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        OverlayRevisionPublishedV2Handler handler = NewHandler(broadcaster, new InMemoryLayoutRepository());

        Guid overlay = Guid.CreateVersion7();
        OverlayRevisionPublishedV2 message = new(
            Overlay: overlay,
            RevisionNumber: 3,
            Name: "Line-1",
            Labels: [new OverlayLabelV2("Hello", 0.2m, 0.3m, 0.4m, 0.5m, 32)],
            PublishedAt: Moment,
            PublishedBy: Guid.CreateVersion7(),
            Metadata: TestMetadata);

        await handler.Handle(message, CancellationToken.None);

        OverlayLifecyclePublishedNotification notification = broadcaster.OverlaysPublished.ShouldHaveSingleItem();
        notification.Overlay.ShouldBe(overlay);
        notification.RevisionNumber.ShouldBe(3);
        notification.Name.ShouldBe("Line-1");
        OverlayLifecycleLabel label = notification.Labels.ShouldHaveSingleItem();
        label.Text.ShouldBe("Hello");
        label.NormalizedX.ShouldBe(0.2m);
        label.NormalizedHeight.ShouldBe(0.5m);
        label.FontSizePx.ShouldBe(32);
        notification.PublishedAt.ShouldBe(Moment);
    }

    /// <summary>Spec 150 (#2345): every label of the set is relayed, in order.</summary>
    [Fact]
    public async Task Relays_every_label_of_the_set_in_order()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        OverlayRevisionPublishedV2Handler handler = NewHandler(broadcaster, new InMemoryLayoutRepository());

        Guid overlay = Guid.CreateVersion7();
        OverlayRevisionPublishedV2 message = new(
            Overlay: overlay,
            RevisionNumber: 1,
            Name: "Line-1",
            Labels:
            [
                new OverlayLabelV2("First", 0.1m, 0.1m, 0.2m, 0.2m, 16),
                new OverlayLabelV2("Second", 0.2m, 0.2m, 0.2m, 0.2m, 20),
            ],
            PublishedAt: Moment,
            PublishedBy: Guid.CreateVersion7(),
            Metadata: TestMetadata);

        await handler.Handle(message, CancellationToken.None);

        OverlayLifecyclePublishedNotification notification = broadcaster.OverlaysPublished.ShouldHaveSingleItem();
        notification.Labels.Count.ShouldBe(2);
        notification.Labels[0].Text.ShouldBe("First");
        notification.Labels[1].Text.ShouldBe("Second");
    }

    /// <summary>FR-010 — referenced by one fab, told to that fab only.</summary>
    [Fact]
    public async Task An_overlay_used_by_one_fab_is_announced_to_that_fab_only()
    {
        Guid overlay = Guid.CreateVersion7();
        InMemoryLayoutRepository layouts = new();
        Seed(layouts, Dresden, overlay, publish: true);
        Seed(layouts, Munich, Guid.CreateVersion7(), publish: true);

        FakeLayoutLifecycleBroadcaster broadcaster = new();
        await NewHandler(broadcaster, layouts).Handle(MessageFor(overlay), CancellationToken.None);

        broadcaster.OverlaysPublished.ShouldHaveSingleItem()
            .Fabs.Select(fab => fab.Value).ShouldBe(["dresden"]);
    }

    /// <summary>
    /// FR-010's other half — "no fab missing". A template shared by two plants
    /// must reach both; they are each displaying it.
    /// </summary>
    [Fact]
    public async Task An_overlay_used_by_both_fabs_is_announced_to_both()
    {
        Guid overlay = Guid.CreateVersion7();
        InMemoryLayoutRepository layouts = new();
        Seed(layouts, Munich, overlay, publish: true);
        Seed(layouts, Dresden, overlay, publish: true);

        FakeLayoutLifecycleBroadcaster broadcaster = new();
        await NewHandler(broadcaster, layouts).Handle(MessageFor(overlay), CancellationToken.None);

        broadcaster.OverlaysPublished.ShouldHaveSingleItem()
            .Fabs.Select(fab => fab.Value).ShouldBe(["munich", "dresden"], ignoreOrder: true);
    }

    /// <summary>
    /// FR-011, and invisible when it works: an overlay nothing references
    /// resolves to an empty set, so the broadcaster sends nothing at all
    /// rather than sending to an empty group.
    /// </summary>
    [Fact]
    public async Task An_overlay_no_layout_references_is_announced_to_nobody()
    {
        InMemoryLayoutRepository layouts = new();
        Seed(layouts, Munich, Guid.CreateVersion7(), publish: true);

        FakeLayoutLifecycleBroadcaster broadcaster = new();
        await NewHandler(broadcaster, layouts)
            .Handle(MessageFor(Guid.CreateVersion7()), CancellationToken.None);

        broadcaster.OverlaysPublished.ShouldHaveSingleItem().Fabs.ShouldBeEmpty();
    }

    /// <summary>
    /// FR-013. A draft's tiles do not count, so the fab whose only use is
    /// unpublished hears nothing — accepted, because it displays the overlay
    /// nowhere.
    /// </summary>
    [Fact]
    public async Task An_overlay_referenced_only_by_a_draft_is_announced_to_nobody()
    {
        Guid overlay = Guid.CreateVersion7();
        InMemoryLayoutRepository layouts = new();
        Seed(layouts, Dresden, overlay, publish: false);

        FakeLayoutLifecycleBroadcaster broadcaster = new();
        await NewHandler(broadcaster, layouts).Handle(MessageFor(overlay), CancellationToken.None);

        broadcaster.OverlaysPublished.ShouldHaveSingleItem().Fabs.ShouldBeEmpty();
    }

    private static OverlayRevisionPublishedV2Handler NewHandler(
        FakeLayoutLifecycleBroadcaster broadcaster, InMemoryLayoutRepository layouts) =>
        new(broadcaster,
            new FabsReferencingOverlayQueryHandler(new InMemoryLayoutQuerySource(layouts)),
            NullLogger<OverlayRevisionPublishedV2Handler>.Instance);

    private static void Seed(
        InMemoryLayoutRepository layouts, FabIdentifier fab, Guid overlay, bool publish)
    {
        LayoutBuilder builder = new LayoutBuilder()
            .WithFab(fab)
            .Named($"L-{Guid.NewGuid():N}"[..12])
            .WithOverlay(OverlayIdentifier.From(overlay))
            .At(FixedMoment);
        Layout layout = builder.Build();
        if (publish)
        {
            layout.Publish(LayoutRevisionNumber.One, builder.Operator, builder.Clock);
        }

        layouts.Add(layout);
    }

    private static OverlayRevisionPublishedV2 MessageFor(Guid overlay) => new(
        Overlay: overlay,
        RevisionNumber: 1,
        Name: "Line-1",
        Labels: [new OverlayLabelV2("Hello", 0.2m, 0.3m, 0.4m, 0.5m, 32)],
        PublishedAt: Moment,
        PublishedBy: Guid.CreateVersion7(),
        Metadata: TestMetadata);
}
