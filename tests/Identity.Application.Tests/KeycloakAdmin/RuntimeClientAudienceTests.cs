using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Application.Tests.Fakes;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Tests.KeycloakAdmin;

/// <summary>
/// Spec 069 — the clients this system creates at runtime also name the API their
/// tokens are for.
///
/// <para>
/// <b>This is the outage guard.</b> Three handlers create Keycloak clients
/// through the Admin API — a kiosk enrolment, a device registration and the
/// first webhook rotation. Those clients are not in
/// <c>smart-sentinel-eye-realm.json</c>, so no realm guard can see them, and
/// <see cref="KeycloakClientRepresentation"/> has no <c>protocolMappers</c>
/// field at all: <see cref="KeycloakClientRepresentation.DefaultClientScopes"/>
/// is the only thing that decides what lands in their tokens.
/// </para>
///
/// <para>
/// <b>Nothing else in this repository would notice them losing it.</b>
/// <c>WebhookBearerValidationIntegrationTests</c> substitutes
/// <c>management-web</c> for a rotated client, so the whole suite stays green
/// while every webhook integration in the field is refused.
/// </para>
///
/// <para>
/// Each handler is asserted twice: that the audience arrives, and that the
/// permissions its bundle grants are still all there. The second half is green
/// today and exists so a later tidy-up cannot trade one for the other —
/// appending the audience must not become replacing the bundle.
/// </para>
/// </summary>
public class RuntimeClientAudienceTests
{
    private const string AudienceScope = "sse-audience";

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-09-04T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Operator =
        OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task An_enrolled_kiosk_names_the_api_its_token_is_for()
    {
        KeycloakClientRepresentation kiosk = await EnrolledKioskAsync();

        kiosk.DefaultClientScopes.ShouldContain(AudienceScope, customMessage: Missing("kiosk"));
    }

    [Fact]
    public async Task An_enrolled_kiosk_keeps_every_permission_its_bundle_grants()
    {
        KeycloakClientRepresentation kiosk = await EnrolledKioskAsync();

        KeycloakScopeBundles.Kiosk.ShouldBeSubsetOf(
            kiosk.DefaultClientScopes, customMessage: Traded("kiosk", nameof(KeycloakScopeBundles.Kiosk)));
    }

    [Fact]
    public async Task A_registered_device_names_the_api_its_token_is_for()
    {
        KeycloakClientRepresentation device = await RegisteredDeviceAsync();

        device.DefaultClientScopes.ShouldContain(AudienceScope, customMessage: Missing("device"));
    }

    [Fact]
    public async Task A_registered_device_keeps_every_permission_its_bundle_grants()
    {
        KeycloakClientRepresentation device = await RegisteredDeviceAsync();

        KeycloakScopeBundles.Device.ShouldBeSubsetOf(
            device.DefaultClientScopes, customMessage: Traded("device", nameof(KeycloakScopeBundles.Device)));
    }

    [Fact]
    public async Task A_rotated_webhook_client_names_the_api_its_token_is_for()
    {
        KeycloakClientRepresentation webhook = await RotatedWebhookClientAsync();

        webhook.DefaultClientScopes.ShouldContain(AudienceScope, customMessage: Missing("webhook integration"));
    }

    [Fact]
    public async Task A_rotated_webhook_client_keeps_every_permission_its_bundle_grants()
    {
        KeycloakClientRepresentation webhook = await RotatedWebhookClientAsync();

        KeycloakScopeBundles.WebhookIntegration.ShouldBeSubsetOf(
            webhook.DefaultClientScopes,
            customMessage: Traded("webhook integration", nameof(KeycloakScopeBundles.WebhookIntegration)));
    }

    private static string Missing(string persona) =>
        $"the {persona} client is created without '{AudienceScope}', so the token it mints does "
        + "not name this API and every API refuses it the moment audience validation is on. This "
        + "client is created at runtime, so it is in no realm file and no realm guard can see it "
        + "(spec 069 FR-005).";

    private static string Traded(string persona, string bundle) =>
        $"the {persona} client no longer carries every scope KeycloakScopeBundles.{bundle} grants. "
        + "The audience is appended to the bundle, not substituted for it — a client that names "
        + "the API but may do nothing is the same outage wearing a different hat.";

    private static async Task<KeycloakClientRepresentation> EnrolledKioskAsync()
    {
        InMemoryRegisteredClientRepository clients = new();
        FakeKeycloakAdminClient keycloak = new();
        EnrollKioskCommandHandler handler = new(
            clients, keycloak, new FakeClock(Now),
            NullLogger<EnrollKioskCommandHandler>.Instance);

        await handler.HandleAsync(
            new EnrollKioskCommand(
                ClientId.From("kiosk-3"), FabIdentifier.From("munich"), Operator),
            CancellationToken.None);

        return keycloak.Created.ShouldHaveSingleItem();
    }

    private static async Task<KeycloakClientRepresentation> RegisteredDeviceAsync()
    {
        InMemoryRegisteredClientRepository clients = new();
        FakeKeycloakAdminClient keycloak = new();
        RegisterDeviceCommandHandler handler = new(
            clients, keycloak, new FakeClock(Now),
            NullLogger<RegisterDeviceCommandHandler>.Instance);

        await handler.HandleAsync(
            new RegisterDeviceCommand("plc", "station-4", FabIdentifier.From("munich"), Operator),
            CancellationToken.None);

        return keycloak.Created.ShouldHaveSingleItem();
    }

    /// <summary>
    /// The <em>first</em> rotation, which is the one that creates the Keycloak
    /// client — <c>Option&lt;int&gt;.None</c> is the "it does not exist yet"
    /// intent (ADR-0113 layer 1). Later rotations only roll the secret and send
    /// no representation at all.
    /// </summary>
    private static async Task<KeycloakClientRepresentation> RotatedWebhookClientAsync()
    {
        InMemoryRegisteredClientRepository clients = new();
        FakeKeycloakAdminClient keycloak = new();
        RotateWebhookClientCommandHandler handler = new(
            clients, keycloak, new FakeEventBus(), new NoOpTransactionalCommit(),
            new FakeClock(Now),
            NullLogger<RotateWebhookClientCommandHandler>.Instance);

        await handler.HandleAsync(
            new RotateWebhookClientCommand(
                "qa", FabIdentifier.From("munich"), Operator, Option<int>.None),
            CancellationToken.None);

        return keycloak.Created.ShouldHaveSingleItem();
    }
}
