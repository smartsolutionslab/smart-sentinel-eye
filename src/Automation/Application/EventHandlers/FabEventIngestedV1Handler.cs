using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber on <see cref="FabEventIngestedV1"/> (spec
/// 006 → 007 bridge). Runs each event through the
/// <see cref="RuleEvaluator"/> and publishes one V1 integration
/// event per resulting action effect.
///
/// <para>
/// The three downstream V1 contracts (<see cref="SystemVariableValueRequestedV1"/>,
/// <see cref="OverlayHighlightRequestedV1"/> and <see cref="WallSceneSwitchRequestedV1"/>)
/// all carry the <c>CausingEventIdentifier</c> so consumers can dedup against
/// Wolverine outbox redelivery.
/// </para>
///
/// <para>
/// The correct dedupe key for that redelivery is the contract's own
/// <c>Metadata.EventIdentifier</c> — a fresh <see cref="Guid.CreateVersion7()"/>
/// minted per published effect, below — and not
/// <c>(OverlayIdentifier, CausingEventIdentifier)</c>. Two highlight rules
/// firing on one overlay for one event produce two
/// <see cref="OverlayHighlightRequestedV1"/> that share both of those and
/// differ only in duration; a consumer keyed on that pair collapses them,
/// and the kiosk never receives the second window to OR against the first
/// (#2214).
/// </para>
/// </summary>
public sealed class FabEventIngestedV1Handler(
    RuleEvaluator evaluator,
    IEventBus events,
    IClock clock,
    ILogger<FabEventIngestedV1Handler> logger)
{
    public async Task Handle(FabEventIngestedV1 message, CancellationToken cancellationToken)
    {
        Ensure.That(message).IsNotNull();

        var (eventIdentifier, fab, source, _, kind, _, ingestedAt, _, _) = message;

        if (!TryParseFab(eventIdentifier, fab, out var parsedFab)
            || !TryEvaluate(message, parsedFab, out var effects)
            || effects.Count == 0)
        {
            return;
        }

        var requestedAt = clock.UtcNow;
        foreach (var effect in effects)
        {
            // RootIngestedAt, not requestedAt: the leg the
            // constitution budgets starts when the plant-floor
            // event was accepted, not when this decision was
            // made. Forwarding it is the only way the service
            // that applies the effect can see both ends
            // (spec 025).
            var metadata = new EventMetadata(Guid.CreateVersion7(), requestedAt, fab, null, ingestedAt);
            var causation = new EffectCausation(requestedAt, eventIdentifier, metadata);
            await PublishEffectAsync(effect, causation, cancellationToken);
        }

        logger.FannedOutActions(effects.Count, eventIdentifier, source, kind);
    }

    private bool TryParseFab(Guid eventIdentifier, string fab, [NotNullWhen(true)] out FabIdentifier? parsedFab)
    {
        // An event that does not say which fab it came from triggers nothing
        // (spec 013 FR-012). Falling back to evaluating every rule is exactly
        // the behaviour #1252 describes, so the absence of a fab must fail
        // closed rather than open.
        if (string.IsNullOrWhiteSpace(fab))
        {
            logger.SkippedEventWithoutFab(eventIdentifier);
            parsedFab = null;
            return false;
        }

        try
        {
            parsedFab = FabIdentifier.From(fab);
            return true;
        }
        catch (ArgumentException exception)
        {
            // Also fails closed, but says so differently: this is not a
            // publisher omitting a fab, it is EventIngestion and Automation
            // disagreeing about what a fab looks like, and it silences every
            // rule for that fab until someone notices. The value and the
            // reason both go in the log, because neither is recoverable from
            // the event identifier alone.
            logger.SkippedEventWithUnparseableFab(exception, eventIdentifier, fab);
            parsedFab = null;
            return false;
        }
    }

    private bool TryEvaluate(
        FabEventIngestedV1 message,
        FabIdentifier parsedFab,
        out IReadOnlyList<RuleActionEffect> effects)
    {
        JsonDocument document;
        try
        {
            document = ParseContext(message);
        }
        catch (JsonException exception)
        {
            logger.SkippedEventWithUnparseablePayload(
                exception,
                message.EventIdentifier,
                message.Payload?.Length ?? 0);
            effects = Array.Empty<RuleActionEffect>();
            return false;
        }

        using (document)
        {
            effects = evaluator.Evaluate(
                parsedFab,
                message.Source,
                message.Kind,
                new EvaluationContext(document.RootElement));
        }

        return true;
    }

    private Task PublishEffectAsync(
        RuleActionEffect effect,
        EffectCausation causation,
        CancellationToken cancellationToken) =>
        effect switch
        {
            RuleActionEffect.SetVariableValue setVariableValue =>
                PublishVariableAsync(setVariableValue, causation, cancellationToken),
            RuleActionEffect.HighlightOverlay highlightOverlay =>
                PublishHighlightAsync(highlightOverlay, causation, cancellationToken),
            RuleActionEffect.SwitchWallScene switchWallScene =>
                PublishSceneSwitchAsync(switchWallScene, causation, cancellationToken),
            _ => Task.CompletedTask,
        };

    private Task PublishVariableAsync(
        RuleActionEffect.SetVariableValue setVariableValue,
        EffectCausation causation,
        CancellationToken cancellationToken)
    {
        var (requestedAt, eventIdentifier, metadata) = causation;
        return events.PublishAsync(
            new SystemVariableValueRequestedV1(
                setVariableValue.Name,
                setVariableValue.Value,
                requestedAt,
                eventIdentifier,
                Metadata: metadata),
            cancellationToken);
    }

    private Task PublishHighlightAsync(
        RuleActionEffect.HighlightOverlay highlightOverlay,
        EffectCausation causation,
        CancellationToken cancellationToken)
    {
        var (requestedAt, eventIdentifier, metadata) = causation;
        return events.PublishAsync(
            new OverlayHighlightRequestedV1(
                highlightOverlay.Overlay,
                highlightOverlay.DurationMs,
                requestedAt,
                eventIdentifier,
                Metadata: metadata),
            cancellationToken);
    }

    private Task PublishSceneSwitchAsync(
        RuleActionEffect.SwitchWallScene switchWallScene,
        EffectCausation causation,
        CancellationToken cancellationToken)
    {
        var (requestedAt, eventIdentifier, metadata) = causation;
        return events.PublishAsync(
            new WallSceneSwitchRequestedV1(
                switchWallScene.Wall,
                switchWallScene.Target,
                switchWallScene.Layout,
                switchWallScene.Rule,
                requestedAt,
                eventIdentifier,
                Metadata: metadata),
            cancellationToken);
    }

    private readonly record struct EffectCausation(
        DateTimeOffset RequestedAt,
        Guid CausingEvent,
        EventMetadata Metadata);

    /// <summary>
    /// Parses a <see cref="JsonDocument"/> whose root is a JSON object
    /// exposing the canonical envelope fields plus the already-canonicalised
    /// payload — so AEL field access (<c>$.source</c>, <c>$.kind</c>,
    /// <c>$.device</c>, <c>$.payload.*</c>) lines up with spec FR-013.
    ///
    /// <para>
    /// Returns the document itself, not a view of it: <see cref="EvaluationContext"/>
    /// does not own what it wraps, so the caller disposes the document once it
    /// is done evaluating (<c>DryRunRuleQueryHandler</c> follows the same
    /// shape).
    /// </para>
    /// </summary>
    private static JsonDocument ParseContext(FabEventIngestedV1 message)
    {
        // Compose the context JSON by hand to avoid double-parsing
        // the payload — it's already a canonical JSON string.
        StringBuilder builder = new();
        builder.Append("{\"source\":");
        builder.Append(JsonSerializer.Serialize(message.Source));
        builder.Append(",\"kind\":");
        builder.Append(JsonSerializer.Serialize(message.Kind));
        builder.Append(",\"device\":");
        builder.Append(JsonSerializer.Serialize(message.Device));
        builder.Append(",\"payload\":");
        builder.Append(message.Payload);
        builder.Append('}');

        return JsonDocument.Parse(builder.ToString(), ContextParseOptions);
    }

    // The payload was validated by EventIngestion at System.Text.Json's default
    // depth (64); the envelope object this method wraps around it is one level
    // more, so the parse here must allow exactly one level beyond that limit —
    // not a raised or unbounded one.
    private const int PayloadMaximumDepth = 64;
    private static readonly JsonDocumentOptions ContextParseOptions = new() { MaxDepth = PayloadMaximumDepth + 1 };
}
