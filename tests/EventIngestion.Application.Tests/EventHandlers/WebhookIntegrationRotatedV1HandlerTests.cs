using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.EventHandlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.EventIngestion.Application.Tests.EventHandlers;

public class WebhookIntegrationRotatedV1HandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public async Task Flips_a_registered_integration_to_JWT_validation()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        (WebhookIntegration integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"),
            Kind.From("QaResult"),
            new FakeClock(Now));
        repo.Add(integration);

        WebhookIntegrationRotatedV1Handler handler = new(
            repo, new FakeClock(Now.AddMinutes(1)),
            NullLogger<WebhookIntegrationRotatedV1Handler>.Instance);

        await handler.Handle(
            new WebhookIntegrationRotatedV1("qa", "webhook-qa", Now.AddMinutes(1), Metadata: TestMetadata),
            CancellationToken.None);

        integration.ValidationMode.ShouldBe(BearerValidationMode.Jwt);
        integration.KeycloakClientId.ShouldBe("webhook-qa");
    }

    [Fact]
    public async Task Unknown_integration_is_a_no_op()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        WebhookIntegrationRotatedV1Handler handler = new(
            repo, new FakeClock(Now),
            NullLogger<WebhookIntegrationRotatedV1Handler>.Instance);

        await handler.Handle(
            new WebhookIntegrationRotatedV1("not-here", "webhook-not-here", Now, Metadata: TestMetadata),
            CancellationToken.None);

        repo.Integrations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Replay_against_already_rotated_integration_is_idempotent()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        (WebhookIntegration integration, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"),
            Kind.From("QaResult"),
            new FakeClock(Now));
        integration.MarkAsRotated("webhook-qa", new FakeClock(Now.AddMinutes(1)));
        repo.Add(integration);

        WebhookIntegrationRotatedV1Handler handler = new(
            repo, new FakeClock(Now.AddMinutes(5)),
            NullLogger<WebhookIntegrationRotatedV1Handler>.Instance);

        await handler.Handle(
            new WebhookIntegrationRotatedV1("qa", "webhook-qa", Now.AddMinutes(5), Metadata: TestMetadata),
            CancellationToken.None);

        integration.RotatedAt.ShouldBe(Now.AddMinutes(1)); // unchanged
    }
}
