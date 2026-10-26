using System.Text.Json;
using Moq;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 464 (#3123). A completed idempotency key never replays the secret of a client that has
/// since been disabled: the rotate route and the device register route both answer 409
/// <c>REPLAYED_CLIENT_DISABLED</c> and do not ask Keycloak for the secret.
/// </summary>
public class ReplayOfDisabledClientEndpointTests
{
    private const string Rotate = "/webhook-integrations/{name}/rotate";

    private const string Register = "/devices/register";

    private const string Disabled = "REPLAYED_CLIENT_DISABLED";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly Guid Resource = new("0190a0f0-0000-7000-8000-0000000000aa");

    [Fact]
    public async Task A_rotate_replay_of_a_disabled_client_is_refused_409_without_reading_the_secret()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource, version: 5);
        var held = host.Held(
            RegisteredClientIdentifier.From(Resource), "webhook-line-7", ClientKind.WebhookIntegration, "dresden", 5);
        held.Disable(host.Clock.Object);
        host.Clients
            .Setup(clients => clients.GetVersionAsync(
                RegisteredClientIdentifier.From(Resource), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<AggregateVersion>.Some(AggregateVersion.From(5)));

        var response = await host.SendAsync(
            "POST",
            Rotate,
            IdentityEndpointHost.Operator(Subject, "dresden"),
            route: new Dictionary<string, string> { ["name"] = "line-7" },
            bodyJson: """{"fabId":"dresden"}""",
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = "key-464", ["If-Match"] = "\"5\"" });

        response.Status.ShouldBe(409);
        TitleOf(response).ShouldBe(Disabled);
        response.Body.ShouldNotContain(IdentityEndpointHost.ReplayedSecret);
        host.Keycloak.Verify(
            keycloak => keycloak.ReadClientSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_device_register_replay_of_a_disabled_client_is_refused_409_without_reading_the_secret()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource);
        var held = host.Held(
            RegisteredClientIdentifier.From(Resource), "plc.dresden.line-1", ClientKind.Device, "dresden", 4);
        held.Disable(host.Clock.Object);

        var response = await host.SendAsync(
            "POST",
            Register,
            IdentityEndpointHost.Operator(Subject, "dresden"),
            bodyJson: """{"deviceType":"plc","deviceIdentifier":"line-1"}""",
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = "key-464" },
            query: "?fabId=dresden");

        response.Status.ShouldBe(409);
        TitleOf(response).ShouldBe(Disabled);
        response.Body.ShouldNotContain(IdentityEndpointHost.ReplayedSecret);
        host.Keycloak.Verify(
            keycloak => keycloak.ReadClientSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static string? TitleOf(IdentityEndpointHost.HostResponse response) =>
        JsonDocument.Parse(response.Body).RootElement.GetProperty("title").GetString();
}
