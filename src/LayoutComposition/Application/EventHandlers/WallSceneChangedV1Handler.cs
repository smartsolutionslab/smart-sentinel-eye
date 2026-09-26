using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber on <see cref="WallSceneChangedV1"/> (spec 258 US1,
/// phase-6 remediation on plan.md §4.3). Relays the change onto the
/// <c>/hubs/layouts</c> SignalR hub via the broadcaster LayoutComposition
/// owns — the same relay shape as <see cref="OverlayRevisionPublishedV1Handler"/>,
/// even though both the publisher and this subscriber live in this one
/// context: what matters is that Wolverine only invokes a subscriber once
/// its message has actually left the Postgres outbox, which happens after —
/// never before — the transaction that raised it commits (ADR-0088). Calling
/// the broadcaster from <see cref="WallSceneSwitchedDomainEventHandler"/>
/// directly, by contrast, ran pre-commit and could broadcast a switch whose
/// write then lost a concurrency conflict and never landed.
/// </summary>
public sealed class WallSceneChangedV1Handler(
    ILayoutLifecycleBroadcaster broadcaster,
    ILogger<WallSceneChangedV1Handler> logger)
{
    public async Task Handle(WallSceneChangedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();

        var (wall, _, current, sceneVersion, _, _, _, _, metadata) = message;

        // This context stamps its own Fab on every WallSceneChangedV1 it
        // publishes, so a missing one would be a bug here rather than an
        // upstream context's incomplete data — but a frame with no fab still
        // cannot be delivered to anyone, so it is dropped and logged rather
        // than broadcast widely, matching every other relay in this file.
        if (string.IsNullOrWhiteSpace(metadata.Fab))
        {
            logger.WallSceneChangedWithoutFab(wall, sceneVersion);

            return;
        }

        await broadcaster.WallSceneChangedAsync(
            new WallSceneChangedNotification(
                FabIdentifier.From(metadata.Fab),
                WallIdentifier.From(wall),
                LayoutIdentifier.From(current),
                SceneVersion.From(sceneVersion)),
            cancellationToken);

        logger.BroadcastWallSceneChanged(wall, sceneVersion);
    }
}
