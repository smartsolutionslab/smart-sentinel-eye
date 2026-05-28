using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Commands;

public class WebhookIntegrationCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Register_persists_the_integration_and_returns_the_plain_token_once()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        RegisterWebhookIntegrationCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<RegisterWebhookIntegrationCommandHandler>.Instance);

        Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError> result =
            await handler.HandleAsync(
                new RegisterWebhookIntegrationCommand(
                    WebhookIntegrationName.From("qa"),
                    Kind.From("QaResult")),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlainToken.ShouldNotBeEmpty();
        repo.Integrations.ShouldHaveSingleItem().Name.Value.ShouldBe("qa");
        repo.Integrations[0].TokenHash.Matches(result.Value.PlainToken).ShouldBeTrue();
    }

    [Fact]
    public async Task Register_with_a_taken_name_returns_WebhookIntegrationNameTaken()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        (WebhookIntegration seeded, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"), Kind.From("QaResult"), new FakeClock(Now));
        repo.Add(seeded);

        RegisterWebhookIntegrationCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<RegisterWebhookIntegrationCommandHandler>.Instance);

        Result<RegisterWebhookIntegrationResult, RegisterWebhookIntegrationError> result =
            await handler.HandleAsync(
                new RegisterWebhookIntegrationCommand(
                    WebhookIntegrationName.From("qa"),
                    Kind.From("QaResult")),
                CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RegisterWebhookIntegrationError.WebhookIntegrationNameTaken>();
    }

    [Fact]
    public async Task Revoke_flips_the_integration_state_and_returns_the_identifier()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        (WebhookIntegration seeded, _) = WebhookIntegration.Register(
            WebhookIntegrationName.From("qa"), Kind.From("QaResult"), new FakeClock(Now));
        repo.Add(seeded);

        RevokeWebhookIntegrationCommandHandler handler = new(
            repo, new FakeClock(Now.AddHours(1)),
            NullLogger<RevokeWebhookIntegrationCommandHandler>.Instance);

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> result =
            await handler.HandleAsync(
                new RevokeWebhookIntegrationCommand(WebhookIntegrationName.From("qa")),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeded.Id);
        repo.Integrations[0].IsRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task Revoke_unknown_integration_returns_WebhookIntegrationNotFound()
    {
        InMemoryWebhookIntegrationRepository repo = new();
        RevokeWebhookIntegrationCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<RevokeWebhookIntegrationCommandHandler>.Instance);

        Result<WebhookIntegrationIdentifier, RevokeWebhookIntegrationError> result =
            await handler.HandleAsync(
                new RevokeWebhookIntegrationCommand(WebhookIntegrationName.From("ghost")),
                CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<RevokeWebhookIntegrationError.WebhookIntegrationNotFound>();
    }
}
