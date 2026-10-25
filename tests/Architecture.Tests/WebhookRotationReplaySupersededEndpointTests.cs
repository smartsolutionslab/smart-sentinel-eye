using System.Text.Json;
using Moq;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 454 (#3096). What <c>POST /webhook-integrations/{name}/rotate</c> does with the version
/// a completed key carries: a replay is answered only while the client is still at that version,
/// the refusal is 409 <c>IDEMPOTENCY_KEY_SUPERSEDED</c>, and a key completed before versions were
/// stored fails closed. The real rotate and replay handlers run; only what sits behind them is a
/// double (see <see cref="IdentityEndpointHost"/>). The unbound replay devices and kiosks keep is
/// pinned by the untouched <c>DeviceAndKioskEndpointCharacterisationTests</c>.
/// </summary>
public class WebhookRotationReplaySupersededEndpointTests
{
    private const string Rotate = "/webhook-integrations/{name}/rotate";

    private const string Superseded = "IDEMPOTENCY_KEY_SUPERSEDED";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly Guid Resource = new("0190a0f0-0000-7000-8000-0000000000aa");

    [Fact]
    public async Task A_completed_key_with_no_stored_version_is_refused_as_superseded_without_asking_the_replay_query()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource);
        host.Held(RegisteredClientIdentifier.From(Resource), "webhook-line-7", ClientKind.WebhookIntegration, "dresden", 5);

        var response = await RotateAsync(host);

        response.Status.ShouldBe(409);
        TitleOf(response).ShouldBe(Superseded);
        host.Clients.Verify(
            clients => clients.GetByIdentifierAsync(It.IsAny<RegisteredClientIdentifier>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_completed_key_whose_stored_version_is_behind_the_client_is_refused_without_reading_the_secret()
    {
        await using var host = new IdentityEndpointHost();
        StoreReplaysAt(host, version: 4);
        HoldAt(host, version: 5);

        var response = await RotateAsync(host);

        response.Status.ShouldBe(409);
        TitleOf(response).ShouldBe(Superseded);
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_refusal_discloses_neither_a_secret_nor_a_version_nor_the_fab()
    {
        await using var host = new IdentityEndpointHost();
        StoreReplaysAt(host, version: 4);
        HoldAt(host, version: 5);

        var response = await RotateAsync(host);

        response.Body.ShouldNotContain(IdentityEndpointHost.ReplayedSecret);
        response.Body.ShouldNotContain("clientSecret");
        response.Body.ShouldNotContain("version");
        response.Body.ShouldNotContain("dresden");
    }

    [Fact]
    public async Task A_completed_key_whose_stored_version_is_still_current_replays_the_original_answer()
    {
        await using var host = new IdentityEndpointHost();
        StoreReplaysAt(host, version: 5);
        HoldAt(host, version: 5);

        var response = await RotateAsync(host);

        response.Status.ShouldBe(200);
        response.Body.ShouldBe(
            $$"""{"registeredClientIdentifier":"{{Resource}}","version":5,"clientId":"webhook-line-7","integrationName":"line-7","fab":"dresden","clientSecret":"{{IdentityEndpointHost.ReplayedSecret}}"}""");
    }

    [Fact]
    public async Task A_rotation_that_lands_while_the_secret_is_being_read_refuses_the_replay_and_returns_no_secret()
    {
        await using var host = new IdentityEndpointHost();
        StoreReplaysAt(host, version: 5);
        HoldAt(host, version: 5);
        host.Clients
            .Setup(clients => clients.GetVersionAsync(
                RegisteredClientIdentifier.From(Resource), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<AggregateVersion>.Some(AggregateVersion.From(6)));

        var response = await RotateAsync(host);

        response.Status.ShouldBe(409);
        TitleOf(response).ShouldBe(Superseded);
        response.Body.ShouldNotContain(IdentityEndpointHost.ReplayedSecret);
        host.Keycloak.Verify(
            keycloak => keycloak.ReadClientSecretAsync("webhook-line-7", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_key_bound_to_a_different_request_is_still_refused_422_before_any_version_is_considered()
    {
        await using var host = new IdentityEndpointHost();
        host.IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdempotencyReservation.Mismatched);

        var response = await RotateAsync(host);

        response.Status.ShouldBe(422);
        TitleOf(response).ShouldBe("IDEMPOTENCY_KEY_REUSED");
        host.Clients.Verify(
            clients => clients.GetVersionAsync(It.IsAny<RegisteredClientIdentifier>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_keyed_rotation_completes_its_key_with_the_version_its_answer_carried()
    {
        await using var host = new IdentityEndpointHost();
        var held = host.Held(
            RegisteredClientIdentifier.From(Guid.NewGuid()), "webhook-line-7", ClientKind.WebhookIntegration, "dresden", 3);
        host.Clients
            .Setup(clients => clients.GetWithinFabAsync(
                FabIdentifier.From("dresden"), ClientId.From("webhook-line-7"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<RegisteredClientAggregate>.Some(held));

        var response = await RotateAsync(host, ifMatch: "\"3\"");

        response.Status.ShouldBe(200);
        var answered = JsonDocument.Parse(response.Body).RootElement.GetProperty("version").GetInt32();
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(
                It.Is<IdempotencyClaim>(claim => claim.Scope.Key.Value == "key-454"),
                held.Id.Value,
                AggregateVersion.From(answered),
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static void StoreReplaysAt(IdentityEndpointHost host, int version) =>
        host.IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdempotencyReservation.CompletedWith(
                Resource, Option<AggregateVersion>.Some(AggregateVersion.From(version))));

    private static void HoldAt(IdentityEndpointHost host, int version)
    {
        host.Held(
            RegisteredClientIdentifier.From(Resource), "webhook-line-7", ClientKind.WebhookIntegration, "dresden", version);
        host.Clients
            .Setup(clients => clients.GetVersionAsync(
                RegisteredClientIdentifier.From(Resource), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<AggregateVersion>.Some(AggregateVersion.From(version)));
    }

    private static string? TitleOf(IdentityEndpointHost.HostResponse response) =>
        JsonDocument.Parse(response.Body).RootElement.GetProperty("title").GetString();

    private static Task<IdentityEndpointHost.HostResponse> RotateAsync(
        IdentityEndpointHost host, string ifMatch = "\"3\"") =>
        host.SendAsync(
            "POST",
            Rotate,
            IdentityEndpointHost.Operator(Subject, "dresden"),
            route: new Dictionary<string, string> { ["name"] = "line-7" },
            bodyJson: """{"fabId":"dresden"}""",
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = "key-454", ["If-Match"] = ifMatch });
}
