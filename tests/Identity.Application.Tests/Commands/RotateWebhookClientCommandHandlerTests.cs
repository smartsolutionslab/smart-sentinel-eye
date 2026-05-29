using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

public class RotateWebhookClientCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static RotateWebhookClientCommand HappyCommand(string name = "qa") =>
        new(name, FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()));

    [Fact]
    public async Task First_rotation_creates_the_Keycloak_client_and_publishes_WebhookIntegrationRotatedV1()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, bus, new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientId.ShouldBe("webhook-qa");
        result.Value.ClientSecret.ShouldBe("secret-webhook-qa");
        repo.Clients.ShouldHaveSingleItem().Kind.ShouldBe(ClientKind.WebhookIntegration);

        WebhookIntegrationRotatedV1 published = bus.Published
            .OfType<WebhookIntegrationRotatedV1>().ShouldHaveSingleItem();
        published.IntegrationName.ShouldBe("qa");
        published.ClientId.ShouldBe("webhook-qa");
    }

    [Fact]
    public async Task Second_rotation_rolls_the_secret_without_creating_a_second_aggregate()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, bus, new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        await handler.HandleAsync(HappyCommand(), CancellationToken.None);
        Result<WebhookClientCredentialsDto, RotateWebhookClientError> second =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        second.IsSuccess.ShouldBeTrue();
        second.Value.ClientSecret.ShouldBe("secret-webhook-qa-rotated");
        repo.Clients.Count.ShouldBe(1);
        repo.Clients[0].LastRotatedAt.ShouldNotBeNull();
        bus.Published.OfType<WebhookIntegrationRotatedV1>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Invalid_integration_name_returns_InvalidIntegrationName()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, bus, new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        // "Has Spaces" yields clientId "webhook-Has Spaces" which
        // fails the ClientId grammar.
        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result = await handler
            .HandleAsync(HappyCommand("Has Spaces"), CancellationToken.None);

        result.Error.ShouldBeOfType<RotateWebhookClientError.InvalidIntegrationName>();
    }

    [Fact]
    public async Task Keycloak_transport_failure_returns_KeycloakUnavailable()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new() { FailNextCall = "Keycloak 500" };
        FakeEventBus bus = new();
        RotateWebhookClientCommandHandler handler = new(
            repo, keycloak, bus, new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(HappyCommand(), CancellationToken.None);

        result.Error.ShouldBeOfType<RotateWebhookClientError.KeycloakUnavailable>();
        repo.Clients.ShouldBeEmpty();
    }
}
