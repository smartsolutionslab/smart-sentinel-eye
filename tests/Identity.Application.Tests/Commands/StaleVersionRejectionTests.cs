using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.Commands;

/// <summary>
/// ADR-0113 Layer 1 for Identity, which after review applies to the webhook
/// rotation only — the device and kiosk disables carry no gate, because a
/// disable is terminal and the repository stops returning the row, so their
/// version can never move while they are still reachable.
///
/// <para>
/// Rotation is an upsert, so the caller states which operation it means and a
/// mismatch is refused rather than resolved the other way. Every rejection
/// test asserts Keycloak was left alone: these are irreversible side effects
/// on a live credential, and a check that ran after them would return the
/// right error having already caused an outage.
/// </para>
///
/// <para>
/// There is deliberately no unit test for the version *chaining* across
/// rotations. The in-memory repository does not reproduce
/// <c>AggregateVersionInterceptor</c>, so every version here is 0 and such a
/// test could not fail; <c>RegisteredClientConcurrencyIntegrationTests</c>
/// covers it against the real interceptor.
/// </para>
/// </summary>
public class StaleVersionRejectionTests
{
    private const int Stale = 41;

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Re_rotating_on_a_stale_version_leaves_the_live_secret_alone()
    {
        (RotateWebhookClientCommandHandler handler, FakeKeycloakAdminClient keycloak, _) = Registered();
        string live = keycloak.CurrentSecrets["webhook-qa"];

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(Update(Stale), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WEBHOOK_CLIENT_STALE");
        keycloak.CurrentSecrets["webhook-qa"].ShouldBe(live);
    }

    [Fact]
    public async Task Asserting_a_version_for_a_client_that_does_not_exist_creates_nothing()
    {
        // The mistyped-integration-name case. Falling through to the register
        // branch would mint a real Keycloak service-account client, with a
        // live credential, for a caller who believed they were rotating.
        InMemoryRegisteredClientRepository clients = new();
        FakeKeycloakAdminClient keycloak = new();

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await Rotator(clients, keycloak, new FakeEventBus())
                .HandleAsync(Update(3), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WEBHOOK_CLIENT_NOT_FOUND");
        clients.Clients.ShouldBeEmpty();
        keycloak.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Asserting_no_client_when_one_exists_does_not_roll_its_secret()
    {
        // The replayed first-time rotation. Version 0 cannot distinguish this
        // from a legitimate re-rotation — the interceptor leaves Added roots at
        // 0 — which is why the intent rides If-None-Match rather than a value.
        (RotateWebhookClientCommandHandler handler, FakeKeycloakAdminClient keycloak, _) = Registered();
        string live = keycloak.CurrentSecrets["webhook-qa"];

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(Create(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WEBHOOK_CLIENT_ALREADY_EXISTS");
        keycloak.CurrentSecrets["webhook-qa"].ShouldBe(live);
    }

    [Fact]
    public async Task A_first_time_rotation_registers_the_client()
    {
        InMemoryRegisteredClientRepository clients = new();
        FakeKeycloakAdminClient keycloak = new();

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await Rotator(clients, keycloak, new FakeEventBus())
                .HandleAsync(Create(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        clients.Clients.ShouldHaveSingleItem().Kind.ShouldBe(ClientKind.WebhookIntegration);
    }

    [Fact]
    public async Task The_matching_version_rolls_the_secret()
    {
        (RotateWebhookClientCommandHandler handler,
            FakeKeycloakAdminClient keycloak,
            InMemoryRegisteredClientRepository clients) = Registered();
        string before = keycloak.CurrentSecrets["webhook-qa"];

        Result<WebhookClientCredentialsDto, RotateWebhookClientError> result =
            await handler.HandleAsync(Update(clients.Clients[0].Version), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        keycloak.CurrentSecrets["webhook-qa"].ShouldNotBe(before);
        clients.Clients.ShouldHaveSingleItem().LastRotatedAt.ShouldNotBeNull();
    }

    private static RotateWebhookClientCommand Create() => Rotation(Option<int>.None);

    private static RotateWebhookClientCommand Update(int version) => Rotation(Option<int>.Some(version));

    private static RotateWebhookClientCommand Rotation(Option<int> expectedVersion) =>
        new("qa", FabIdentifier.From("munich"), OperatorIdentifier.From(Guid.CreateVersion7()), expectedVersion);

    private static RotateWebhookClientCommandHandler Rotator(
        InMemoryRegisteredClientRepository clients, FakeKeycloakAdminClient keycloak, FakeEventBus bus) =>
        new(clients, keycloak, bus, new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

    /// <summary>
    /// Drives a real first-time rotation so the Keycloak fake holds a genuine
    /// secret for the client. Seeding the aggregate directly would leave
    /// <c>CurrentSecrets</c> empty and every "left alone" assertion vacuous.
    /// </summary>
    private static (RotateWebhookClientCommandHandler, FakeKeycloakAdminClient, InMemoryRegisteredClientRepository) Registered()
    {
        InMemoryRegisteredClientRepository clients = new();
        FakeKeycloakAdminClient keycloak = new();
        RotateWebhookClientCommandHandler handler = Rotator(clients, keycloak, new FakeEventBus());

        handler.HandleAsync(Create(), CancellationToken.None)
            .GetAwaiter().GetResult().IsSuccess.ShouldBeTrue();

        return (handler, keycloak, clients);
    }
}
