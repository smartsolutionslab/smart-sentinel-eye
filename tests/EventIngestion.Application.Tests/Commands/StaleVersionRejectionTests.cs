using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Commands;

/// <summary>
/// ADR-0113 Layer 1 for EventIngestion. Revoke is this context's only
/// mutate-existing command, so there is one stale case rather than the
/// three-to-five the other contexts carry.
///
/// <para>
/// The rejection test also asserts the integration was left alone — the
/// check is only worth having if it runs *before* the mutation, and a
/// handler that rejected afterwards would return the right error while
/// having already revoked a live integration.
/// </para>
/// </summary>
public class StaleVersionRejectionTests
{
    private const int Stale = 41;

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Revoke_rejects_a_stale_version_and_leaves_the_integration_live()
    {
        (InMemoryWebhookIntegrationRepository integrations, WebhookIntegration seeded) = Seeded();

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> result =
            await Revoker(integrations).HandleAsync(
                new RevokeWebhookIntegrationCommand(seeded.Name, Stale), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WEBHOOK_INTEGRATION_STALE");
        result.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        integrations.Integrations.ShouldHaveSingleItem().IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task The_stale_message_names_the_integration_and_both_versions()
    {
        (InMemoryWebhookIntegrationRepository integrations, WebhookIntegration seeded) = Seeded();

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> result =
            await Revoker(integrations).HandleAsync(
                new RevokeWebhookIntegrationCommand(seeded.Name, Stale), CancellationToken.None);

        result.Error.Message.ShouldContain("qa");
        result.Error.Message.ShouldContain(Stale.ToString(CultureInfo.InvariantCulture));
        result.Error.Message.ShouldContain("Re-read");
        result.Error.Message.ShouldNotContain("Try again");
    }

    [Fact]
    public async Task The_matching_version_is_accepted()
    {
        (InMemoryWebhookIntegrationRepository integrations, WebhookIntegration seeded) = Seeded();

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> result =
            await Revoker(integrations).HandleAsync(
                new RevokeWebhookIntegrationCommand(seeded.Name, seeded.Version), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        integrations.Integrations.ShouldHaveSingleItem().IsRevoked.ShouldBeTrue();
    }

    /// <summary>
    /// Version 7 rather than 0. At 0 the accept path cannot distinguish a real
    /// comparison from a handler that ignored the aggregate and compared
    /// <c>default(int)</c> to <c>default(int)</c> — which is what every
    /// assertion in this file did before the fake reproduced the interceptor.
    /// </summary>
    [Fact]
    public async Task The_gate_compares_the_real_version_not_the_default()
    {
        (InMemoryWebhookIntegrationRepository integrations, WebhookIntegration seeded) = Seeded(version: 7);

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> atZero =
            await Revoker(integrations).HandleAsync(
                new RevokeWebhookIntegrationCommand(seeded.Name, 0), CancellationToken.None);

        atZero.IsFailure.ShouldBeTrue();
        atZero.Error.Code.ShouldBe("WEBHOOK_INTEGRATION_STALE");
        atZero.Error.Message.ShouldContain("7");

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> atSeven =
            await Revoker(integrations).HandleAsync(
                new RevokeWebhookIntegrationCommand(seeded.Name, 7), CancellationToken.None);

        atSeven.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// The bump itself, which the handler's contract depends on: without it
    /// the second writer's expectation would still match and Layer 1 would
    /// never fire in production.
    /// </summary>
    [Fact]
    public async Task A_committed_revoke_moves_the_version_off_the_value_the_caller_held()
    {
        (InMemoryWebhookIntegrationRepository integrations, WebhookIntegration seeded) = Seeded(version: 3);

        await Revoker(integrations).HandleAsync(
            new RevokeWebhookIntegrationCommand(seeded.Name, 3), CancellationToken.None);

        integrations.Integrations.ShouldHaveSingleItem().Version.ShouldBe(4);
    }

    /// <summary>
    /// The retry case. A client whose response was lost to a gateway timeout
    /// re-sends the only version it holds — the pre-revoke one — so gating the
    /// repeat would answer 409 for a change that caller already landed, and
    /// they could not tell it apart from a real concurrent edit. Revoke is
    /// idempotent by design, and that has to survive the concurrency gate.
    /// </summary>
    [Fact]
    public async Task A_repeat_revoke_holding_the_pre_revoke_version_still_succeeds()
    {
        (InMemoryWebhookIntegrationRepository integrations, WebhookIntegration seeded) = Seeded(version: 3);
        int heldByTheCaller = seeded.Version;

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> first =
            await Revoker(integrations).HandleAsync(
                new RevokeWebhookIntegrationCommand(seeded.Name, heldByTheCaller), CancellationToken.None);

        // The retry re-sends the version it still holds, which the committed
        // revoke has now moved past. That is a genuinely stale request by the
        // gate's own definition — the assertion is that idempotency outranks
        // it, not that the version happened not to move.
        integrations.Integrations[0].Version.ShouldBeGreaterThan(heldByTheCaller);

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> retry =
            await Revoker(integrations).HandleAsync(
                new RevokeWebhookIntegrationCommand(seeded.Name, heldByTheCaller), CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        retry.IsSuccess.ShouldBeTrue();
        retry.Value.ShouldBe(first.Value);
        integrations.Integrations.ShouldHaveSingleItem().IsRevoked.ShouldBeTrue();
    }

    private static RevokeWebhookIntegrationCommandHandler Revoker(
        InMemoryWebhookIntegrationRepository integrations) =>
        new(integrations, new FakeClock(Now.AddHours(1)),
            NullLogger<RevokeWebhookIntegrationCommandHandler>.Instance);

    // Seed, not Add: these tests act on an integration that already exists in
    // the database, so its next save bumps. Add models a row being created
    // now, which the interceptor leaves at 0.
    private static (InMemoryWebhookIntegrationRepository, WebhookIntegration) Seeded(int version = 0)
    {
        InMemoryWebhookIntegrationRepository integrations = new();
        (WebhookIntegration seeded, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"), Kind.From("QaResult"), new FakeClock(Now));
        integrations.Seed(seeded, version);

        return (integrations, seeded);
    }
}
