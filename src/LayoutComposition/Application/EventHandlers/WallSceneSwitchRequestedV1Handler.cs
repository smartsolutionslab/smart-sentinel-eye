using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
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

        var (wall, _, _, rule, _, causingEventIdentifier, _) = message;

        var trace = new SwitchRequestTrace(Wall: wall, Rule: rule, CausingEvent: causingEventIdentifier);

        var resolved = await ResolveAsync(message, trace, cancellationToken);
        if (!resolved.HasValue)
        {
            return;
        }
        (var wallIdentifier, var targetWall, var parsedTarget, var ruleIdentifier, var causingEvent) = resolved.Value;

        var reserved = await dedup.TryReserveAsync(ruleIdentifier, causingEvent, wallIdentifier, cancellationToken);
        if (!reserved)
        {
            logger.WallSwitchRequestDuplicate(wall, rule, causingEventIdentifier);
            return;
        }

        if (DropsBeforeLookup(targetWall, parsedTarget, trace))
        {
            return;
        }

        var planned = new PlannedSwitch(
            targetWall,
            parsedTarget,
            new SceneSwitchCause.Rule(ruleIdentifier, causingEvent));
        await ApplySwitchAsync(planned, trace, cancellationToken);
    }

    private async Task<Option<ResolvedSwitch>> ResolveAsync(
        WallSceneSwitchRequestedV1 message,
        SwitchRequestTrace trace,
        CancellationToken cancellationToken)
    {
        var (_, target, targetLayout, _, _, _, metadata) = message;

        var fab = TryResolveFab(metadata, trace);
        if (!fab.HasValue)
        {
            return Option<ResolvedSwitch>.None;
        }

        var loaded =
            await TryLoadWall(fab.Value, trace, cancellationToken);
        if (!loaded.HasValue)
        {
            return Option<ResolvedSwitch>.None;
        }

        var parsedTarget = TryParseTarget(target, targetLayout, trace);
        if (!parsedTarget.HasValue)
        {
            return Option<ResolvedSwitch>.None;
        }

        var identifiers =
            TryResolveRuleAndEvent(trace);
        if (!identifiers.HasValue)
        {
            return Option<ResolvedSwitch>.None;
        }

        return Option<ResolvedSwitch>.Some(new ResolvedSwitch(
            loaded.Value.Identifier,
            loaded.Value.Wall,
            parsedTarget.Value,
            identifiers.Value.Rule,
            identifiers.Value.CausingEvent));
    }

    private Option<SceneTarget> TryParseTarget(string target, Guid? targetLayout, SwitchRequestTrace trace)
    {
        try
        {
            return Option<SceneTarget>.Some(ParseTarget(target, targetLayout));
        }
        catch (ArgumentException)
        {
            logger.WallSwitchRequestMalformedTarget(trace.Wall, trace.Rule, trace.CausingEvent, target);
            return Option<SceneTarget>.None;
        }
    }

    private bool DropsBeforeLookup(Wall targetWall, SceneTarget parsedTarget, SwitchRequestTrace trace)
    {
        if (parsedTarget is not SceneTarget.Layout named)
        {
            return false;
        }

        if (!targetWall.Scenes.Contains(named.Value))
        {
            logger.WallSwitchRequestSceneNotInSet(trace.Wall, trace.Rule, trace.CausingEvent, named.Value);
            return true;
        }

        if (named.Value == targetWall.Showing)
        {
            // Same no-op shortcut as the manual handler's own: whether the
            // target is still Published is irrelevant to a switch that
            // doesn't move Showing at all, so this must not depend on the
            // publishable lookup below.
            logger.RuleWallSceneSwitchWasNoOp(trace.Wall, trace.Rule, trace.CausingEvent);
            return true;
        }

        return false;
    }

    private Option<FabIdentifier> TryResolveFab(EventMetadata? metadata, SwitchRequestTrace trace)
    {
        if (string.IsNullOrWhiteSpace(metadata?.Fab))
        {
            logger.WallSwitchRequestWithoutFab(trace.Wall, trace.Rule, trace.CausingEvent);
            return Option<FabIdentifier>.None;
        }

        try
        {
            return Option<FabIdentifier>.Some(FabIdentifier.From(metadata.Fab));
        }
        catch (ArgumentException ex)
        {
            logger.WallSwitchRequestWithUnusableFab(ex, metadata.Fab, trace.Wall, trace.Rule, trace.CausingEvent);
            return Option<FabIdentifier>.None;
        }
    }

    private async Task<Option<(WallIdentifier Identifier, Wall Wall)>> TryLoadWall(
        FabIdentifier fab, SwitchRequestTrace trace, CancellationToken cancellationToken)
    {
        WallIdentifier wallIdentifier;
        try
        {
            wallIdentifier = WallIdentifier.From(trace.Wall);
        }
        catch (ArgumentException)
        {
            // An empty wall identifier matches no wall, so this is the same
            // named drop as any other unknown wall (Automation never sends
            // one in practice; this is the defensive case).
            logger.WallSwitchRequestForUnknownWall(trace.Wall, trace.Rule, trace.CausingEvent);
            return Option<(WallIdentifier Identifier, Wall Wall)>.None;
        }

        var found = await walls.FindAsync(wallIdentifier, [fab], cancellationToken);
        if (found.HasValue)
        {
            return Option<(WallIdentifier Identifier, Wall Wall)>.Some((wallIdentifier, found.Value));
        }

        await LogMissingWall(wallIdentifier, fab, trace, cancellationToken);
        return Option<(WallIdentifier Identifier, Wall Wall)>.None;
    }

    private async Task LogMissingWall(
        WallIdentifier wallIdentifier,
        FabIdentifier requestFab,
        SwitchRequestTrace trace,
        CancellationToken cancellationToken)
    {
        var actualFab = await walls.FindFabAsync(wallIdentifier, cancellationToken);
        if (actualFab.HasValue)
        {
            logger.WallSwitchRequestForWallInAnotherFab(trace.Wall, trace.Rule, trace.CausingEvent, requestFab, actualFab.Value);
        }
        else
        {
            logger.WallSwitchRequestForUnknownWall(trace.Wall, trace.Rule, trace.CausingEvent);
        }
    }

    private Option<(RuleIdentifier Rule, CausingEventIdentifier CausingEvent)> TryResolveRuleAndEvent(
        SwitchRequestTrace trace)
    {
        try
        {
            return Option<(RuleIdentifier Rule, CausingEventIdentifier CausingEvent)>.Some(
                (RuleIdentifier.From(trace.Rule), CausingEventIdentifier.From(trace.CausingEvent)));
        }
        catch (ArgumentException)
        {
            // Defensive, like the empty-wall case above: Automation never
            // sends an empty rule or causing-event identifier in practice.
            logger.WallSwitchRequestWithInvalidIdentifiers(trace.Wall, trace.Rule, trace.CausingEvent);
            return Option<(RuleIdentifier Rule, CausingEventIdentifier CausingEvent)>.None;
        }
    }

    private async Task ApplySwitchAsync(
        PlannedSwitch planned,
        SwitchRequestTrace trace,
        CancellationToken cancellationToken)
    {
        var (targetWall, parsedTarget, cause) = planned;

        var publishable =
            await lookup.PublishedAmong(targetWall.Scenes, targetWall.Fab, cancellationToken);

        if (parsedTarget is SceneTarget.Layout notPublished && !publishable.Contains(notPublished.Value))
        {
            logger.WallSwitchRequestSceneNotPublished(trace.Wall, trace.Rule, trace.CausingEvent, notPublished.Value);
            return;
        }

        var switched = targetWall.SwitchTo(parsedTarget, publishable, cause, clock);
        await walls.SaveAsync(cancellationToken);

        if (switched.HasValue)
        {
            logger.RuleSwitchedWallScene(trace.Wall, trace.Rule, trace.CausingEvent);
        }
        else
        {
            logger.RuleWallSceneSwitchWasNoOp(trace.Wall, trace.Rule, trace.CausingEvent);
        }
    }

    // The three wire identifiers every drop is logged with, kept as the raw
    // values because they are logged precisely when they fail to parse.
    private readonly record struct SwitchRequestTrace(Guid Wall, Guid Rule, Guid CausingEvent);

    private sealed record PlannedSwitch(Wall Wall, SceneTarget Target, SceneSwitchCause.Rule Cause);

    private sealed record ResolvedSwitch(
        WallIdentifier Identifier,
        Wall Wall,
        SceneTarget Target,
        RuleIdentifier Rule,
        CausingEventIdentifier CausingEvent);

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
