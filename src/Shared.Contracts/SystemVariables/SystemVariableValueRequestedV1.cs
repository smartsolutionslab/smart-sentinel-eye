namespace SmartSentinelEye.Shared.Contracts.SystemVariables;

/// <summary>
/// Integration event raised by Automation (spec 007) requesting
/// SystemVariables (spec 005) to set <paramref name="Name"/> to
/// <paramref name="Value"/>. The SystemVariables consumer is
/// idempotent on the (<paramref name="Name"/>,
/// <paramref name="CausingEventIdentifier"/>) pair so Wolverine
/// outbox redelivery doesn't double-set.
///
/// <para>
/// <paramref name="CausingEventIdentifier"/> is the
/// <c>FabEventIngestedV1.EventIdentifier</c> that triggered the
/// rule. Carrying it down the chain lets the audit log + replay
/// path correlate cause and effect.
/// </para>
/// </summary>
public sealed record SystemVariableValueRequestedV1(
    string Name,
    string Value,
    DateTimeOffset RequestedAt,
    Guid CausingEventIdentifier) : IIntegrationEvent;
