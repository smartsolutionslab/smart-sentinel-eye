using JasperFx;
using JasperFx.CodeGeneration;
using SmartSentinelEye.Identity.Application.EventHandlers;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace SmartSentinelEye.Identity.Infrastructure;

/// <summary>
/// Spec 328 (#2629): a Keycloak outage must not dead-letter a webhook-client
/// revocation after Wolverine's default three immediate attempts, because a
/// dead-lettered revocation leaves the client enabled — and, per ADR-0160, its
/// tokens accepted — until someone replays it by hand. Scoped to exactly
/// <see cref="WebhookIntegrationRevokedV1"/>'s own handler chain, mirroring
/// <c>WallSceneSwitchFailurePolicy</c> (spec 296) — no shared default.
///
/// <para>
/// <b>The ladder:</b> 1 minute, then 2 minutes, then 5 minutes x
/// 12 — 14 scheduled retries, 15 attempts before the default dead-letter
/// behaviour (unchanged) takes over. The first two steps are sized to outlast
/// a Keycloak pod restart or a short database failover, the likeliest outages
/// and the ones the old budget already lost. The 5-minute cap bounds
/// post-recovery exposure — once Keycloak is back, the revoked client stays
/// enabled (and, per ADR-0160, its tokens stay accepted) for at most one cap
/// plus Wolverine's 5 s scheduled-job poll plus ADR-0160's 5 s snapshot
/// refresh — against a 3600 s realm token lifetime. Twelve repeats give
/// roughly an hour of total coverage, a judgment call: long enough to span a
/// restart, failover, node drain/reboot or maintenance window, short enough
/// that the residual dead letter after it stays the documented manual
/// recovery (spec 264), not a silent one.
/// </para>
///
/// <para>
/// <b>Why <c>ScheduleRetry</c> and not
/// <c>RetryWithCooldown</c>:</b> decompiling the installed Wolverine 6.40.0
/// showed <c>RetryWithCooldown</c> delays with an uncancellable
/// <c>Task.Delay</c> inline on the listener — wrong at the minute scale, since
/// it would hold the queue's one listener for up to an hour and block
/// graceful shutdown. <c>ScheduleRetry</c> reschedules the envelope instead,
/// which frees the listener and survives a restart with its scheduled time
/// intact.
/// </para>
///
/// <para>
/// <b>What makes that reschedule durable:</b> decompiling Wolverine 6.40.0
/// further shows every RabbitMQ listener defaults to <c>Mode=Inline</c> —
/// set unconditionally in Wolverine's own endpoint constructor — and that a
/// reschedule on an <c>Inline</c> listener never acknowledges the original
/// broker delivery, because RabbitMQ has no native scheduling hook for
/// Wolverine to settle it through; the delivery would sit unacknowledged on
/// the channel for the whole ladder above. <c>WolverineDefaults.AddWolverineForContext</c>
/// opts this one listener into <c>UseDurableInbox()</c> (Wolverine's
/// <c>Durable</c> mode) specifically so the original delivery is acknowledged
/// and the retry is tracked in Identity's Postgres inbox instead — scoped to
/// this message type alone, not a change to any other listener's default.
/// </para>
/// </summary>
public sealed class WebhookIntegrationRevokedFailurePolicy : IHandlerPolicy
{
    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        HandlerChain? chain = chains.FirstOrDefault(
            candidate => candidate.MessageType == typeof(WebhookIntegrationRevokedV1));

        TimeSpan[] ladder =
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            .. Enumerable.Repeat(TimeSpan.FromMinutes(5), 12),
        ];

        chain?.OnException<WebhookClientDisableFailedException>().ScheduleRetry(ladder);
    }
}
