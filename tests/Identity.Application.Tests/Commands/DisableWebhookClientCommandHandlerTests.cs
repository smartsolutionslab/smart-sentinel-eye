using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Domain.Tests.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

/// <summary>
/// Spec 264 (#2206). A line-for-line mirror of
/// <see cref="DisableKioskCommandHandlerTests"/> (plan.md §5.2): same shape,
/// same order of failure precedence, only the Kind and the error codes
/// differ. Red: compile, until <c>DisableWebhookClientCommand</c> and its
/// handler exist.
/// </summary>
public class DisableWebhookClientCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    private static void Seed(
        InMemoryRegisteredClientRepository repo, ClientKind kind, string clientId, string fab = "munich")
    {
        RegisteredClientAggregate aggregate = new RegisteredClientBuilder()
            .WithClientId(clientId)
            .WithKind(kind)
            .WithFab(fab)
            .WithClock(Now)
            .Build();
        repo.Seed(aggregate);
    }

    [Fact]
    public async Task Happy_path_disables_the_webhook_client_in_Keycloak_and_marks_the_row()
    {
        InMemoryRegisteredClientRepository repo = new();
        Seed(repo, ClientKind.WebhookIntegration, "webhook-w");
        FakeKeycloakAdminClient keycloak = new();

        DisableWebhookClientCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now.AddHours(1)),
            NullLogger<DisableWebhookClientCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableWebhookClientError> result = await handler.HandleAsync(
            new DisableWebhookClientCommand(ClientId.From("webhook-w"), FabIdentifier.From("munich")),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        keycloak.Disabled.ShouldContain("webhook-w");
        repo.Clients[0].DisabledAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Unknown_clientId_returns_WebhookClientNotFound()
    {
        InMemoryRegisteredClientRepository repo = new();
        FakeKeycloakAdminClient keycloak = new();
        DisableWebhookClientCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<DisableWebhookClientCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableWebhookClientError> result = await handler.HandleAsync(
            new DisableWebhookClientCommand(ClientId.From("webhook-ghost"), FabIdentifier.From("munich")),
            CancellationToken.None);

        result.Error.ShouldBeOfType<DisableWebhookClientError.WebhookClientNotFound>();
        keycloak.Disabled.ShouldBeEmpty();
    }

    /// <summary>
    /// A client registered under this clientId but as a different Kind (e.g. a
    /// device happens to share the derived name) must not be disabled by this
    /// path — mirrors DisableKiosk's own Kind gate.
    /// </summary>
    [Fact]
    public async Task Non_webhook_clientId_returns_WebhookClientNotFound()
    {
        InMemoryRegisteredClientRepository repo = new();
        Seed(repo, ClientKind.Device, "webhook-w");
        FakeKeycloakAdminClient keycloak = new();

        DisableWebhookClientCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<DisableWebhookClientCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableWebhookClientError> result = await handler.HandleAsync(
            new DisableWebhookClientCommand(ClientId.From("webhook-w"), FabIdentifier.From("munich")),
            CancellationToken.None);

        result.Error.ShouldBeOfType<DisableWebhookClientError.WebhookClientNotFound>();
        keycloak.Disabled.ShouldBeEmpty();
    }

    /// <summary>
    /// The client exists; the caller may not know that. The refusal must be
    /// the same one an unknown clientId produces, so the answer cannot be used
    /// to enumerate another fab's webhook integrations.
    /// </summary>
    [Fact]
    public async Task Another_fabs_webhook_client_returns_WebhookClientNotFound_and_is_not_disabled()
    {
        InMemoryRegisteredClientRepository repo = new();
        Seed(repo, ClientKind.WebhookIntegration, "webhook-w", "dresden");
        FakeKeycloakAdminClient keycloak = new();

        DisableWebhookClientCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now),
            NullLogger<DisableWebhookClientCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableWebhookClientError> result = await handler.HandleAsync(
            new DisableWebhookClientCommand(ClientId.From("webhook-w"), FabIdentifier.From("munich")),
            CancellationToken.None);

        result.Error.ShouldBeOfType<DisableWebhookClientError.WebhookClientNotFound>();
        keycloak.Disabled.ShouldBeEmpty();
        repo.Clients[0].DisabledAt.ShouldBeNull();
    }

    [Fact]
    public async Task Keycloak_transport_failure_returns_KeycloakUnavailable_and_leaves_the_row_undisabled()
    {
        InMemoryRegisteredClientRepository repo = new();
        Seed(repo, ClientKind.WebhookIntegration, "webhook-w");
        FakeKeycloakAdminClient keycloak = new() { FailNextCall = "Keycloak 500" };

        DisableWebhookClientCommandHandler handler = new(
            repo, keycloak, new FakeClock(Now.AddHours(1)),
            NullLogger<DisableWebhookClientCommandHandler>.Instance);

        Result<RegisteredClientIdentifier, DisableWebhookClientError> result = await handler.HandleAsync(
            new DisableWebhookClientCommand(ClientId.From("webhook-w"), FabIdentifier.From("munich")),
            CancellationToken.None);

        result.Error.ShouldBeOfType<DisableWebhookClientError.KeycloakUnavailable>();
        repo.Clients[0].DisabledAt.ShouldBeNull(
            "Keycloak first, then the row — a failed Keycloak call must not disable the row anyway");
    }

    /// <summary>
    /// A cancellation is not a business failure — it must propagate rather
    /// than be folded into <c>KeycloakUnavailable</c>, which
    /// <c>DisableKioskCommandHandler</c>'s own <c>catch (Exception ex) when
    /// (ex is not OperationCanceledException)</c> guarantees.
    /// </summary>
    [Fact]
    public async Task OperationCanceledException_from_Keycloak_propagates_rather_than_becoming_KeycloakUnavailable()
    {
        InMemoryRegisteredClientRepository repo = new();
        Seed(repo, ClientKind.WebhookIntegration, "webhook-w");

        DisableWebhookClientCommandHandler handler = new(
            repo, new OperationCancelingKeycloakAdminClient(), new FakeClock(Now.AddHours(1)),
            NullLogger<DisableWebhookClientCommandHandler>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(() => handler.HandleAsync(
            new DisableWebhookClientCommand(ClientId.From("webhook-w"), FabIdentifier.From("munich")),
            CancellationToken.None));

        repo.Clients[0].DisabledAt.ShouldBeNull();
    }

    /// <summary>
    /// Redelivery of the same message finds the row already disabled:
    /// <see cref="IRegisteredClientRepository.GetWithinFabAsync"/> excludes
    /// Disabled rows, so the second delivery gets the same
    /// <c>WebhookClientNotFound</c> a repeat <c>DisableKioskCommandHandler</c>
    /// call gets — not a bespoke success path (spec 264 phase-6 review). The
    /// underlying idempotency property still holds: no second Keycloak call,
    /// no state corruption, and <c>DisabledAt</c> unmoved.
    /// </summary>
    [Fact]
    public async Task A_repeat_disable_reports_WebhookClientNotFound_and_does_not_move_DisabledAt()
    {
        InMemoryRegisteredClientRepository repo = new();
        Seed(repo, ClientKind.WebhookIntegration, "webhook-w");
        FakeKeycloakAdminClient keycloak = new();

        DisableWebhookClientCommandHandler first = new(
            repo, keycloak, new FakeClock(Now.AddHours(1)),
            NullLogger<DisableWebhookClientCommandHandler>.Instance);
        await first.HandleAsync(
            new DisableWebhookClientCommand(ClientId.From("webhook-w"), FabIdentifier.From("munich")),
            CancellationToken.None);
        DateTimeOffset firstDisabledAt = repo.Clients[0].DisabledAt!.Value;

        DisableWebhookClientCommandHandler second = new(
            repo, keycloak, new FakeClock(Now.AddHours(2)),
            NullLogger<DisableWebhookClientCommandHandler>.Instance);
        Result<RegisteredClientIdentifier, DisableWebhookClientError> result = await second.HandleAsync(
            new DisableWebhookClientCommand(ClientId.From("webhook-w"), FabIdentifier.From("munich")),
            CancellationToken.None);

        result.Error.ShouldBeOfType<DisableWebhookClientError.WebhookClientNotFound>();
        repo.Clients[0].DisabledAt!.Value.ShouldBe(firstDisabledAt);
    }

    /// <summary>
    /// Throws <see cref="OperationCanceledException"/> from every write call —
    /// only <see cref="DisableClientAsync"/> is exercised by this test class.
    /// A minimal, test-local double rather than a <see cref="FakeKeycloakAdminClient"/>
    /// extension, since nothing else in this suite needs to inject a
    /// cancellation from Keycloak.
    /// </summary>
    private sealed class OperationCancelingKeycloakAdminClient : IKeycloakAdminClient
    {
        public Task<KeycloakClientCredentials> CreateClientAsync(
            KeycloakClientRepresentation representation, string fabGroupPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> RotateClientSecretAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> ReadClientSecretAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisableClientAsync(string clientId, CancellationToken cancellationToken) =>
            throw new OperationCanceledException();

        public Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
            string parentPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> StripInheritedRealmRolesAsync(string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
