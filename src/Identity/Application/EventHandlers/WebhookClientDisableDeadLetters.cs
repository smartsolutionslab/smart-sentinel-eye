using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SmartSentinelEye.Identity.Application.EventHandlers;

/// <summary>
/// Counts webhook-client disables that exhausted their retry ladder and were
/// dead-lettered (spec 434, #3080). A dead-lettered disable leaves the client
/// enabled until someone replays it by hand, so the 15th attempt has to be
/// visible to an operator.
/// </summary>
public static class WebhookClientDisableDeadLetters
{
    /// <summary>
    /// Registered by <c>AddIdentityInfrastructure</c>. A meter nobody registers
    /// records into nothing and raises no error.
    /// </summary>
    public const string MeterName = "SmartSentinelEye.Identity.WebhookClientDisable";

    public const string MetricName = "sse.identity.webhook_client_disable.dead_lettered";

    /// <summary>Values: <c>revocation | race_disable</c> — the chain that dead-lettered.</summary>
    public const string MessageTag = "message";

    public const string Revocation = "revocation";

    public const string RaceDisable = "race_disable";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>(
        name: MetricName,
        unit: "{message}",
        description: "Webhook-client disables dead-lettered after the full retry ladder, by message kind.");

    public static void Record(string message) =>
        DeadLettered.Add(1, new TagList { { MessageTag, message } });
}
