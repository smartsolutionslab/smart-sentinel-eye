using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.LayoutComposition.Domain.Wall.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber on <see cref="WallSceneSwitchRequestedV1"/> (spec 296
/// US1, plan.md §3.1) — the rule-driven counterpart of
/// <c>SwitchWallSceneCommandHandler</c>. Applies against whatever is
/// current, with no expected version (PD-1, FR-006): there is no caller on
/// this path to answer with a <c>409</c>, so every refusal in FR-010 is a
/// named warning instead, and a duplicate delivery (FR-012) is a dedup hit
/// rather than a second switch.
///
/// <para>
/// <b>FR-011.</b> A lost optimistic-concurrency race on
/// <see cref="IWallRepository.SaveAsync"/> is left to escape <see cref="Handle"/>
/// uncaught — catching it here and returning would let Wolverine flush the
/// <c>WallSceneChangedV1</c> the domain-event handler already captured on
/// the ambient outbox context, announcing a switch that rolled back (spec
/// §1.11). <c>WallSceneSwitchFailurePolicy</c> (LayoutComposition.Infrastructure)
/// maps the escaped exception to the dead-letter queue with no retry —
/// ADR-0113 forbids retrying a conflicting write automatically.
/// </para>
/// </summary>
public sealed class WallSceneSwitchRequestedV1Handler(
    IWallRepository walls,
    ILayoutPublicationLookup lookup,
    IWallSwitchRequestDedupStore dedup,
    IClock clock,
    ILogger<WallSceneSwitchRequestedV1Handler> logger)
{
    public async Task Handle(WallSceneSwitchRequestedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();

        var (wall, target, targetLayout, rule, _, causingEventIdentifier, metadata) = message;

        Option<FabIdentifier> fab = TryResolveFab(metadata, wall, rule, causingEventIdentifier);
        if (!fab.HasValue)
        {
            return;
        }

        Option<(WallIdentifier Identifier, Wall Wall)> loaded =
            await TryLoadWall(wall, fab.Value, rule, causingEventIdentifier, cancellationToken);
        if (!loaded.HasValue)
        {
            return;
        }
        (WallIdentifier wallIdentifier, Wall targetWall) = loaded.Value;

        SceneTarget parsedTarget;
        try
        {
            parsedTarget = ParseTarget(target, targetLayout);
        }
        catch (ArgumentException)
        {
            logger.WallSwitchRequestMalformedTarget(wall, rule, causingEventIdentifier, target);
            return;
        }

        Option<(RuleIdentifier Rule, CausingEventIdentifier CausingEvent)> identifiers =
            TryResolveRuleAndEvent(wall, rule, causingEventIdentifier);
        if (!identifiers.HasValue)
        {
            return;
        }
        (RuleIdentifier ruleIdentifier, CausingEventIdentifier causingEvent) = identifiers.Value;

        bool reserved = await dedup.TryReserveAsync(ruleIdentifier, causingEvent, wallIdentifier, cancellationToken);
        if (!reserved)
        {
            logger.WallSwitchRequestDuplicate(wall, rule, causingEventIdentifier);
            return;
        }

        if (parsedTarget is SceneTarget.Layout notInSet && !targetWall.Scenes.Contains(notInSet.Value))
        {
            logger.WallSwitchRequestSceneNotInSet(wall, rule, causingEventIdentifier, notInSet.Value);
            return;
        }

        if (parsedTarget is SceneTarget.Layout alreadyShowing && alreadyShowing.Value == targetWall.Showing)
        {
            // Same no-op shortcut as the manual handler's own: whether the
            // target is still Published is irrelevant to a switch that
            // doesn't move Showing at all, so this must not depend on the
            // publishable lookup below.
            logger.RuleWallSceneSwitchWasNoOp(wall, rule, causingEventIdentifier);
            return;
        }

        await ApplySwitchAsync(
            targetWall, parsedTarget, ruleIdentifier, causingEvent, wall, rule, causingEventIdentifier, cancellationToken);
    }

    private Option<FabIdentifier> TryResolveFab(EventMetadata? metadata, Guid wall, Guid rule, Guid causingEventIdentifier)
    {
        if (string.IsNullOrWhiteSpace(metadata?.Fab))
        {
            logger.WallSwitchRequestWithoutFab(wall, rule, causingEventIdentifier);
            return Option<FabIdentifier>.None;
        }

        try
        {
            return Option<FabIdentifier>.Some(FabIdentifier.From(metadata.Fab));
        }
        catch (ArgumentException ex)
        {
            logger.WallSwitchRequestWithUnusableFab(ex, metadata.Fab, wall, rule, causingEventIdentifier);
            return Option<FabIdentifier>.None;
        }
    }

    private async Task<Option<(WallIdentifier Identifier, Wall Wall)>> TryLoadWall(
        Guid wall, FabIdentifier fab, Guid rule, Guid causingEventIdentifier, CancellationToken cancellationToken)
    {
        WallIdentifier wallIdentifier;
        try
        {
            wallIdentifier = WallIdentifier.From(wall);
        }
        catch (ArgumentException)
        {
            // An empty wall identifier matches no wall, so this is the same
            // named drop as any other unknown wall (Automation never sends
            // one in practice; this is the defensive case).
            logger.WallSwitchRequestForUnknownWall(wall, rule, causingEventIdentifier);
            return Option<(WallIdentifier Identifier, Wall Wall)>.None;
        }

        Option<Wall> found = await walls.FindAsync(wallIdentifier, [fab], cancellationToken);
        if (found.HasValue)
        {
            return Option<(WallIdentifier Identifier, Wall Wall)>.Some((wallIdentifier, found.Value));
        }

        await LogMissingWall(wallIdentifier, fab, wall, rule, causingEventIdentifier, cancellationToken);
        return Option<(WallIdentifier Identifier, Wall Wall)>.None;
    }

    private async Task LogMissingWall(
        WallIdentifier wallIdentifier, FabIdentifier requestFab, Guid wall, Guid rule, Guid causingEventIdentifier,
        CancellationToken cancellationToken)
    {
        Option<FabIdentifier> actualFab = await walls.FindFabAsync(wallIdentifier, cancellationToken);
        if (actualFab.HasValue)
        {
            logger.WallSwitchRequestForWallInAnotherFab(wall, rule, causingEventIdentifier, requestFab, actualFab.Value);
        }
        else
        {
            logger.WallSwitchRequestForUnknownWall(wall, rule, causingEventIdentifier);
        }
    }

    private Option<(RuleIdentifier Rule, CausingEventIdentifier CausingEvent)> TryResolveRuleAndEvent(
        Guid wall, Guid rule, Guid causingEventIdentifier)
    {
        try
        {
            return Option<(RuleIdentifier Rule, CausingEventIdentifier CausingEvent)>.Some(
                (RuleIdentifier.From(rule), CausingEventIdentifier.From(causingEventIdentifier)));
        }
        catch (ArgumentException)
        {
            // Defensive, like the empty-wall case above: Automation never
            // sends an empty rule or causing-event identifier in practice.
            logger.WallSwitchRequestWithInvalidIdentifiers(wall, rule, causingEventIdentifier);
            return Option<(RuleIdentifier Rule, CausingEventIdentifier CausingEvent)>.None;
        }
    }

    private async Task ApplySwitchAsync(
        Wall targetWall, SceneTarget parsedTarget, RuleIdentifier ruleIdentifier, CausingEventIdentifier causingEvent,
        Guid wall, Guid rule, Guid causingEventIdentifier, CancellationToken cancellationToken)
    {
        IReadOnlySet<LayoutIdentifier> publishable =
            await lookup.PublishedAmong(targetWall.Scenes, targetWall.Fab, cancellationToken);

        if (parsedTarget is SceneTarget.Layout notPublished && !publishable.Contains(notPublished.Value))
        {
            logger.WallSwitchRequestSceneNotPublished(wall, rule, causingEventIdentifier, notPublished.Value);
            return;
        }

        Option<WallSceneSwitchedDomainEvent> switched = targetWall.SwitchTo(
            parsedTarget, publishable, new SceneSwitchCause.Rule(ruleIdentifier, causingEvent), clock);
        await walls.SaveAsync(cancellationToken);

        if (switched.HasValue)
        {
            logger.RuleSwitchedWallScene(wall, rule, causingEventIdentifier);
        }
        else
        {
            logger.RuleWallSceneSwitchWasNoOp(wall, rule, causingEventIdentifier);
        }
    }

    // "Next" / "Layout" (PascalCase) — WallSceneSwitchRequestedV1's own wire
    // literals, distinct from the manual switch request body's lowercase
    // "next" / "layout" (WallEndpoints.Commands): two different wire shapes
    // that happen to share a word.
    private static SceneTarget ParseTarget(string target, Guid? targetLayout) =>
        target switch
        {
            WallSceneSwitchRequestedV1.NextTarget when targetLayout is null =>
                new SceneTarget.Next(),
            WallSceneSwitchRequestedV1.LayoutTarget when targetLayout is { } layout =>
                new SceneTarget.Layout(LayoutIdentifier.From(layout)),
            _ => throw new ArgumentException($"Malformed SwitchWallScene target '{target}'.", nameof(target)),
        };
}
