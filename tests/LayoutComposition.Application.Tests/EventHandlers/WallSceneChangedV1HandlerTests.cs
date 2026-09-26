using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

/// <summary>
/// Phase-6 remediation on spec 258 US1 (plan.md §4.3): the
/// <c>WallSceneChanged</c> hub push moved here, off
/// <c>WallSceneSwitchedDomainEventHandler</c>, so it only fires once the
/// <c>WallSceneChangedV1</c> message has actually left the Postgres outbox —
/// i.e. after the write that raised it committed.
/// </summary>
public class WallSceneChangedV1HandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    private static EventMetadata MetadataFor(string? fab) => new(
        Guid.CreateVersion7(), Moment, fab, Guid.CreateVersion7());

    [Fact]
    public async Task Calls_broadcaster_WallSceneChangedAsync_with_the_wall_scene_and_version()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        WallSceneChangedV1Handler handler = new(broadcaster, NullLogger<WallSceneChangedV1Handler>.Instance);

        Guid wall = Guid.CreateVersion7();
        Guid current = Guid.CreateVersion7();
        WallSceneChangedV1 message = new(
            Wall: wall,
            PreviousLayout: Guid.CreateVersion7(),
            CurrentLayout: current,
            SceneVersion: 3L,
            Cause: "Operator",
            Rule: null,
            CausingEventIdentifier: null,
            ChangedAt: Moment,
            Metadata: MetadataFor("munich"));

        await handler.Handle(message, CancellationToken.None);

        WallSceneChangedNotification notification = broadcaster.WallSceneChanged.ShouldHaveSingleItem();
        notification.Fab.ShouldBe(FabIdentifier.From("munich"));
        notification.Wall.ShouldBe(WallIdentifier.From(wall));
        notification.Showing.ShouldBe(LayoutIdentifier.From(current));
        notification.SceneVersion.ShouldBe(SceneVersion.From(3L));
    }

    [Fact]
    public async Task A_message_with_no_fab_is_not_broadcast()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        WallSceneChangedV1Handler handler = new(broadcaster, NullLogger<WallSceneChangedV1Handler>.Instance);

        WallSceneChangedV1 message = new(
            Wall: Guid.CreateVersion7(),
            PreviousLayout: Guid.CreateVersion7(),
            CurrentLayout: Guid.CreateVersion7(),
            SceneVersion: 1L,
            Cause: "Operator",
            Rule: null,
            CausingEventIdentifier: null,
            ChangedAt: Moment,
            Metadata: MetadataFor(null));

        await handler.Handle(message, CancellationToken.None);

        broadcaster.WallSceneChanged.ShouldBeEmpty();
    }
}
