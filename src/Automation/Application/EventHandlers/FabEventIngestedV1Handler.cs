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
/// </summary>
public sealed class FabEventIngestedV1Handler(
    RuleEvaluator evaluator,
    IEventBus events,
    IClock clock,
    ILogger<FabEventIngestedV1Handler> logger)
{
    public async Task Handle(FabEventIngestedV1 message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        EvaluationContext context = BuildContext(message);
        IReadOnlyList<RuleActionEffect> effects = evaluator.Evaluate(
            message.Source, message.Kind, context);
        if (effects.Count == 0) return;

        DateTimeOffset requestedAt = clock.UtcNow;
        foreach (RuleActionEffect effect in effects)
        {
            switch (effect)
            {
                case RuleActionEffect.SetVariableValue setVariableValue:
                    await events.PublishAsync(
                        new SystemVariableValueRequestedV1(
                            setVariableValue.Name, setVariableValue.Value, requestedAt, message.EventIdentifier,
                            Metadata: new EventMetadata(Guid.CreateVersion7(), requestedAt, message.Fab, null)),
                        cancellationToken).ConfigureAwait(false);
                    break;

                case RuleActionEffect.HighlightOverlay highlightOverlay:
                    await events.PublishAsync(
                        new OverlayHighlightRequestedV1(
                            highlightOverlay.Overlay, highlightOverlay.DurationMs, requestedAt, message.EventIdentifier,
                            Metadata: new EventMetadata(Guid.CreateVersion7(), requestedAt, message.Fab, null)),
                        cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        Log.FannedOutActions(logger, effects.Count, message.EventIdentifier, message.Source, message.Kind);
    }

    /// <summary>
    /// Builds an <see cref="EvaluationContext"/> whose root is a
    /// JSON object exposing the canonical envelope fields plus the
    /// already-canonicalised payload — so AEL field access
    /// (<c>$.source</c>, <c>$.kind</c>, <c>$.device</c>,
    /// <c>$.payload.*</c>) lines up with spec FR-013.
    /// </summary>
    private static EvaluationContext BuildContext(FabEventIngestedV1 message)
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

        JsonDocument doc = JsonDocument.Parse(builder.ToString());
        return new EvaluationContext(doc.RootElement);
    }
}
