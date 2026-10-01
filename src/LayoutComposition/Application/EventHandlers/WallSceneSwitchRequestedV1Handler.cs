using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.LayoutComposition.Domain.Wall.Events;
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

        if (string.IsNullOrWhiteSpace(metadata?.Fab))
        {
            logger.WallSwitchRequestWithoutFab(wall, rule, causingEventIdentifier);
            return;
        }

        FabIdentifier requestFab;
        try
        {
            requestFab = FabIdentifier.From(metadata.Fab);
        }
        catch (ArgumentException)
        {
            logger.WallSwitchRequestWithoutFab(wall, rule, causingEventIdentifier);
            return;
        }

        WallIdentifier wallIdentifier = WallIdentifier.From(wall);
        Option<Wall> found = await walls.FindAsync(wallIdentifier, [requestFab], cancellationToken);
        if (!found.HasValue)
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
            return;
        }

        Wall targetWall = found.Value;

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

        RuleIdentifier ruleIdentifier = RuleIdentifier.From(rule);
        CausingEventIdentifier causingEvent = CausingEventIdentifier.From(causingEventIdentifier);

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
    // literals (its XML doc), distinct from the manual switch request body's
    // lowercase "next" / "layout" (WallEndpoints.Commands): two different
    // wire shapes that happen to share a word.
    private const string NextTargetLiteral = "Next";
    private const string LayoutTargetLiteral = "Layout";

    private static SceneTarget ParseTarget(string target, Guid? targetLayout) =>
        target switch
        {
            NextTargetLiteral when targetLayout is null =>
                new SceneTarget.Next(),
            LayoutTargetLiteral when targetLayout is { } layout =>
                new SceneTarget.Layout(LayoutIdentifier.From(layout)),
            _ => throw new ArgumentException($"Malformed SwitchWallScene target '{target}'.", nameof(target)),
        };
}
