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

    private static RevokeWebhookIntegrationCommandHandler Revoker(
        InMemoryWebhookIntegrationRepository integrations) =>
        new(integrations, new FakeClock(Now.AddHours(1)),
            NullLogger<RevokeWebhookIntegrationCommandHandler>.Instance);

    private static (InMemoryWebhookIntegrationRepository, WebhookIntegration) Seeded()
    {
        InMemoryWebhookIntegrationRepository integrations = new();
        (WebhookIntegration seeded, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"), Kind.From("QaResult"), new FakeClock(Now));
        integrations.Add(seeded);

        return (integrations, seeded);
    }
}
