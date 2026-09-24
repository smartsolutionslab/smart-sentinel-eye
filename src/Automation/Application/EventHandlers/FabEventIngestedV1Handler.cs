using SmartSentinelEye.Automation.Domain.Rule;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Application.Evaluation;
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
/// The two downstream V1 contracts (<see cref="SystemVariableValueRequestedV1"/>
/// and <see cref="OverlayHighlightRequestedV1"/>) both carry the
/// <c>CausingEventIdentifier</c> so consumers can dedup against
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

        // An event that does not say which fab it came from triggers nothing
        // (spec 013 FR-012). Falling back to evaluating every rule is exactly
        // the behaviour #1252 describes, so the absence of a fab must fail
        // closed rather than open.
        if (string.IsNullOrWhiteSpace(fab))
        {
            logger.SkippedEventWithoutFab(eventIdentifier);
            return;
        }

        FabIdentifier parsedFab;
        try
        {
            parsedFab = FabIdentifier.From(fab);
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
            return;
        }

        JsonDocument document = ParseContext(message);
        IReadOnlyList<RuleActionEffect> effects;
        using (document)
        {
            effects = evaluator.Evaluate(
                parsedFab, source, kind, new EvaluationContext(document.RootElement));
        }

        if (effects.Count == 0)
        {
            return;
        }

        DateTimeOffset requestedAt = clock.UtcNow;
        foreach (RuleActionEffect effect in effects)
        {
            switch (effect)
            {
                case RuleActionEffect.SetVariableValue setVariableValue:
                    await events.PublishAsync(
                        new SystemVariableValueRequestedV1(
                            setVariableValue.Name, setVariableValue.Value, requestedAt, eventIdentifier,
                            // RootIngestedAt, not requestedAt: the leg the
                            // constitution budgets starts when the plant-floor
                            // event was accepted, not when this decision was
                            // made. Forwarding it is the only way the service
                            // that applies the effect can see both ends
                            // (spec 025).
                            Metadata: new EventMetadata(
                                Guid.CreateVersion7(), requestedAt, fab, null, ingestedAt)),
                        cancellationToken);
                    break;

                case RuleActionEffect.HighlightOverlay highlightOverlay:
                    await events.PublishAsync(
                        new OverlayHighlightRequestedV1(
                            highlightOverlay.Overlay, highlightOverlay.DurationMs, requestedAt, eventIdentifier,
                            // Same reasoning as the variable effect above.
                            Metadata: new EventMetadata(
                                Guid.CreateVersion7(), requestedAt, fab, null, ingestedAt)),
                        cancellationToken);
                    break;
            }
        }

        logger.FannedOutActions(effects.Count, eventIdentifier, source, kind);
    }

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

        return JsonDocument.Parse(builder.ToString());
    }
}
