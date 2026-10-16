using System.Security.Claims;
using Moq;
using SmartSentinelEye.Identity.Api.Requests;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what <c>POST /devices/register</c> and <c>POST /kiosks/enroll</c> answer, in which order
/// they refuse, what they hand the idempotency store and what a replay returns (spec 367, #2932,
/// FR-003 and FR-004), so that splitting <c>Register</c> and <c>Enroll</c> under ADR-0084's thirty
/// lines cannot move a guard, change a scope or let a retry reach Keycloak. Security-sensitive:
/// both endpoints mint a Keycloak client secret and return it.
///
/// <para>
/// The real command and replay handlers run; only the repository, the Keycloak admin port, the
/// fab guard and the idempotency store behind them are doubles. A transposed argument inside a
/// moved lambda therefore changes what Keycloak is asked to create, what the store is asked to
/// reserve, or what the replay returns, and fails here. Problem bodies are compared as the exact
/// JSON. A caller the fab guard refuses surfaces as <see cref="FabAuthorizationException"/> thrown
/// out of the delegate: the global exception handler maps it to 403 in the running service, and
/// an in-process drive does not include that handler, so the throw is what is pinned.
/// </para>
/// </summary>
public class DeviceAndKioskEndpointCharacterisationTests
{
    private const string Register = "/devices/register";

    private const string Enroll = "/kiosks/enroll";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private const string KeyEmptyBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"IDEMPOTENCY_KEY_MALFORMED","status":400,"detail":"Idempotency-Key was present but empty."}""";

    private const string UnusableFab = "Not A Fab";

    private const string FabRefusal = "value: must be lowercase letters, digits, or '-' and start with a letter (Parameter 'value')";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly Guid Resource = new("0190a0f0-0000-7000-8000-0000000000aa");

    [Fact]
    public async Task Register_reads_a_malformed_idempotency_key_before_it_parses_the_fab_or_asks_the_guard()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RegisterAsync(host, UnusableFab, key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.FabGuard.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
        host.Clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_refuses_a_malformed_idempotency_key_without_asking_the_guard_for_a_valid_fab()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RegisterAsync(host, "dresden", key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.FabGuard.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_refuses_an_unusable_fab_with_the_fab_message_before_the_guard_is_asked()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RegisterAsync(host, UnusableFab);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            400,
            "DEVICE_INVALID_INPUT",
            FabRefusal,
            BadRequestType));
        host.FabGuard.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_lets_the_fab_guards_refusal_escape_before_the_store_or_keycloak_is_touched()
    {
        await using var host = new IdentityEndpointHost();
        host.GuardRefuses("bremen");

        var refusal = await Should.ThrowAsync<FabAuthorizationException>(
            RegisterAsync(host, "bremen", key: "key-1"));

        refusal.FabId.ShouldBe("bremen");
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
        host.Clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_keyed_register_reserves_the_route_the_caller_the_fab_and_the_body()
    {
        await using var host = new IdentityEndpointHost();
        RegisterDeviceRequest body = new("plc", "line-1");

        var response = await RegisterAsync(host, "dresden", key: "key-1");

        response.Status.ShouldBe(201);
        var scope = host.BegunScope();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /devices/register");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(body));
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(IsSubject(), "dresden", It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(
                It.Is<IdempotencyClaim>(claim => claim.Scope.Key.Value == "key-1"),
                Added(host).Id.Value,
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.Verify(
            store => store.ReleaseAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_unkeyed_register_answers_201_with_the_credentials_and_never_touches_the_store()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RegisterAsync(host, "dresden");

        var added = Added(host);
        response.Status.ShouldBe(201);
        response.Location.ShouldBe("/devices/plc.dresden.line-1");
        response.Body.ShouldBe(
            $$"""{"registeredClientIdentifier":"{{added.Id.Value}}","clientId":"plc.dresden.line-1","deviceType":"plc","deviceIdentifier":"line-1","fab":"dresden","clientSecret":"{{IdentityEndpointHost.CreatedSecret}}"}""");
        added.ClientId.Value.ShouldBe("plc.dresden.line-1");
        added.Kind.ShouldBe(ClientKind.Device);
        added.Fab.Value.ShouldBe("dresden");
        added.Registration.By.Value.ShouldBe(Subject);
        host.Keycloak.Verify(
            keycloak => keycloak.CreateClientAsync(
                It.Is<KeycloakClientRepresentation>(representation =>
                    representation.ClientId == "plc.dresden.line-1"
                    && representation.Attributes["sse.kind"] == "device"
                    && representation.Attributes["sse.deviceType"] == "plc"
                    && representation.Attributes["sse.deviceIdentifier"] == "line-1"
                    && representation.Attributes["sse.fab"] == "dresden"),
                "/fabs/dresden",
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.Clients.Verify(clients => clients.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_keyed_register_the_handler_refuses_answers_the_handlers_problem_and_releases_the_key()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RegisterAsync(host, "dresden", key: "key-1", deviceType: "robot");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            400,
            "DEVICE_INVALID_TYPE",
            "deviceType 'robot' is not allowed; expected: plc | inference.",
            BadRequestType));
        host.IdempotencyStore.Verify(
            store => store.ReleaseAsync(
                It.Is<IdempotencyClaim>(claim => claim.Scope.Key.Value == "key-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_replayed_register_takes_its_type_and_identifier_from_the_retry_body_and_never_creates_a_client()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource);
        host.Held(
            RegisteredClientIdentifier.From(Resource), "plc.dresden.line-1", ClientKind.Device, "dresden", 4);

        var response = await RegisterAsync(
            host, "dresden", key: "key-1", deviceType: "inference", deviceIdentifier: "cam-9");

        response.Status.ShouldBe(201);
        response.Location.ShouldBe("/devices/plc.dresden.line-1");
        response.Body.ShouldBe(
            $$"""{"registeredClientIdentifier":"{{Resource}}","clientId":"plc.dresden.line-1","deviceType":"inference","deviceIdentifier":"cam-9","fab":"dresden","clientSecret":"{{IdentityEndpointHost.ReplayedSecret}}"}""");
        host.Keycloak.Verify(
            keycloak => keycloak.ReadClientSecretAsync("plc.dresden.line-1", It.IsAny<CancellationToken>()),
            Times.Once);
        host.Keycloak.Verify(
            keycloak => keycloak.CreateClientAsync(
                It.IsAny<KeycloakClientRepresentation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Clients.Verify(clients => clients.Add(It.IsAny<RegisteredClientAggregate>()), Times.Never);
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_replayed_register_whose_registration_is_gone_answers_the_replay_handlers_problem()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource);

        var response = await RegisterAsync(host, "dresden", key: "key-1");

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            409,
            "REPLAYED_REGISTRATION_MISSING",
            $"The registration '{Resource}' recorded against this Idempotency-Key no longer exists.",
            ConflictType));
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_replayed_register_whose_client_is_gone_from_keycloak_answers_the_replay_handlers_problem()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource);
        host.Held(
            RegisteredClientIdentifier.From(Resource), "plc.dresden.line-1", ClientKind.Device, "dresden", 4);
        host.Keycloak
            .Setup(keycloak => keycloak.ReadClientSecretAsync("plc.dresden.line-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeycloakClientNotFoundException("plc.dresden.line-1"));

        var response = await RegisterAsync(host, "dresden", key: "key-1");

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            409,
            "REPLAYED_CLIENT_MISSING",
            "Keycloak no longer holds client 'plc.dresden.line-1', so its secret cannot be replayed.",
            ConflictType));
        host.Keycloak.Verify(
            keycloak => keycloak.CreateClientAsync(
                It.IsAny<KeycloakClientRepresentation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Enroll_reads_a_malformed_idempotency_key_before_it_parses_the_fab_or_asks_the_guard()
    {
        await using var host = new IdentityEndpointHost();

        var response = await EnrollAsync(host, UnusableFab, key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.FabGuard.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
        host.Clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Enroll_refuses_a_malformed_idempotency_key_without_asking_the_guard_for_a_valid_fab()
    {
        await using var host = new IdentityEndpointHost();

        var response = await EnrollAsync(host, "dresden", key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.FabGuard.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Enroll_refuses_an_unusable_fab_with_the_fab_message_before_the_guard_is_asked()
    {
        await using var host = new IdentityEndpointHost();

        var response = await EnrollAsync(host, UnusableFab);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            400,
            "KIOSK_INVALID_INPUT",
            FabRefusal,
            BadRequestType));
        host.FabGuard.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Enroll_lets_the_fab_guards_refusal_escape_before_the_store_or_keycloak_is_touched()
    {
        await using var host = new IdentityEndpointHost();
        host.GuardRefuses("bremen");

        var refusal = await Should.ThrowAsync<FabAuthorizationException>(
            EnrollAsync(host, "bremen", key: "key-1"));

        refusal.FabId.ShouldBe("bremen");
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
        host.Clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_keyed_enroll_reserves_the_route_the_caller_the_fab_and_the_body()
    {
        await using var host = new IdentityEndpointHost();
        EnrollKioskRequest body = new("lobby-1");

        var response = await EnrollAsync(host, "dresden", key: "key-1");

        response.Status.ShouldBe(201);
        var scope = host.BegunScope();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /kiosks/enroll");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(body));
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(IsSubject(), "dresden", It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(
                It.Is<IdempotencyClaim>(claim => claim.Scope.Key.Value == "key-1"),
                Added(host).Id.Value,
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.Verify(
            store => store.ReleaseAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_unkeyed_enroll_answers_201_with_the_credentials_and_never_touches_the_store()
    {
        await using var host = new IdentityEndpointHost();

        var response = await EnrollAsync(host, "dresden");

        var added = Added(host);
        response.Status.ShouldBe(201);
        response.Location.ShouldBe("/kiosks/kiosk.dresden.lobby-1");
        response.Body.ShouldBe(
            $$"""{"registeredClientIdentifier":"{{added.Id.Value}}","clientId":"kiosk.dresden.lobby-1","fab":"dresden","clientSecret":"{{IdentityEndpointHost.CreatedSecret}}"}""");
        added.ClientId.Value.ShouldBe("kiosk.dresden.lobby-1");
        added.Kind.ShouldBe(ClientKind.Kiosk);
        added.Fab.Value.ShouldBe("dresden");
        added.Registration.By.Value.ShouldBe(Subject);
        host.Keycloak.Verify(
            keycloak => keycloak.CreateClientAsync(
                It.Is<KeycloakClientRepresentation>(representation =>
                    representation.ClientId == "kiosk.dresden.lobby-1"
                    && representation.Attributes["sse.kind"] == "kiosk"
                    && representation.Attributes["sse.fab"] == "dresden"),
                "/fabs/dresden",
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.Clients.Verify(clients => clients.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_keyed_enroll_the_handler_refuses_answers_the_handlers_problem_and_releases_the_key()
    {
        await using var host = new IdentityEndpointHost();

        var response = await EnrollAsync(host, "dresden", key: "key-1", name: "Lobby One");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            400,
            "KIOSK_INVALID_NAME",
            "name rejected: each hyphen-separated segment must be one or more letters or digits.",
            BadRequestType));
        host.IdempotencyStore.Verify(
            store => store.ReleaseAsync(
                It.Is<IdempotencyClaim>(claim => claim.Scope.Key.Value == "key-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_replayed_enroll_returns_every_field_from_the_server_and_never_creates_a_client()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource);
        host.Held(
            RegisteredClientIdentifier.From(Resource), "kiosk.dresden.lobby-1", ClientKind.Kiosk, "dresden", 4);

        var response = await EnrollAsync(host, "dresden", key: "key-1", name: "another-name");

        response.Status.ShouldBe(201);
        response.Location.ShouldBe("/kiosks/kiosk.dresden.lobby-1");
        response.Body.ShouldBe(
            $$"""{"registeredClientIdentifier":"{{Resource}}","clientId":"kiosk.dresden.lobby-1","fab":"dresden","clientSecret":"{{IdentityEndpointHost.ReplayedSecret}}"}""");
        host.Keycloak.Verify(
            keycloak => keycloak.ReadClientSecretAsync("kiosk.dresden.lobby-1", It.IsAny<CancellationToken>()),
            Times.Once);
        host.Keycloak.Verify(
            keycloak => keycloak.CreateClientAsync(
                It.IsAny<KeycloakClientRepresentation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Clients.Verify(clients => clients.Add(It.IsAny<RegisteredClientAggregate>()), Times.Never);
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_replayed_enroll_whose_registration_is_gone_answers_the_replay_handlers_problem()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource);

        var response = await EnrollAsync(host, "dresden", key: "key-1");

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            409,
            "REPLAYED_REGISTRATION_MISSING",
            $"The registration '{Resource}' recorded against this Idempotency-Key no longer exists.",
            ConflictType));
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/devices/", "Device", "device")]
    [InlineData("/kiosks/", "Kiosk", "kiosk")]
    public async Task A_list_without_a_fab_spans_every_fab_the_caller_holds_and_never_asks_the_guard(
        string route,
        string kind,
        string prefix)
    {
        await using var host = SeededHost(kind, prefix);

        var response = await host.SendAsync("GET", route, IdentityEndpointHost.Operator(Subject, "munich", "dresden"));

        response.Status.ShouldBe(200);
        IdentityEndpointHost.ClientIds(response).ShouldBe(
            [prefix + "-munich", prefix + "-dresden"], ignoreOrder: true);
        host.FabGuard.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/devices/", "Device", "device")]
    [InlineData("/kiosks/", "Kiosk", "kiosk")]
    public async Task A_list_naming_a_held_fab_asks_the_guard_once_and_narrows_to_that_fab(
        string route,
        string kind,
        string prefix)
    {
        await using var host = SeededHost(kind, prefix);

        var response = await host.SendAsync(
            "GET", route, IdentityEndpointHost.Operator(Subject, "munich", "dresden"), query: "?fabId=munich");

        response.Status.ShouldBe(200);
        IdentityEndpointHost.ClientIds(response).ShouldBe([prefix + "-munich"]);
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(IsSubject(), "munich", It.IsAny<CancellationToken>()),
            Times.Once);
        host.FabGuard.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/devices/", "Device", "device")]
    [InlineData("/kiosks/", "Kiosk", "kiosk")]
    public async Task A_list_naming_a_fab_the_caller_does_not_hold_lets_the_guards_refusal_escape_before_the_store_is_read(
        string route,
        string kind,
        string prefix)
    {
        await using var host = SeededHost(kind, prefix);
        host.GuardRefuses("bremen");

        var refusal = await Should.ThrowAsync<FabAuthorizationException>(
            host.SendAsync("GET", route, IdentityEndpointHost.Operator(Subject, "munich"), query: "?fabId=bremen"));

        refusal.FabId.ShouldBe("bremen");
        host.QuerySource.Reads.ShouldBe(0);
    }

    [Theory]
    [InlineData("/devices/", "Device", "device")]
    [InlineData("/kiosks/", "Kiosk", "kiosk")]
    public async Task A_list_skips_a_group_that_is_not_a_usable_fab_and_lists_the_fabs_that_are(
        string route,
        string kind,
        string prefix)
    {
        await using var host = SeededHost(kind, prefix);

        var response = await host.SendAsync(
            "GET", route, IdentityEndpointHost.Operator(Subject, "Not-A-Fab", "munich"));

        response.Status.ShouldBe(200);
        IdentityEndpointHost.ClientIds(response).ShouldBe([prefix + "-munich"]);
    }

    [Theory]
    [InlineData("/devices/", "Device", "device", "DEVICE_INVALID_INPUT")]
    [InlineData("/kiosks/", "Kiosk", "kiosk", "KIOSK_INVALID_INPUT")]
    public async Task A_list_with_no_usable_fab_answers_400_with_the_routes_own_code_and_never_reads_the_store(
        string route,
        string kind,
        string prefix,
        string code)
    {
        await using var host = SeededHost(kind, prefix);

        var unusableGroup = await host.SendAsync("GET", route, IdentityEndpointHost.Operator(Subject, "Not-A-Fab"));
        var unusableNamedFab = await host.SendAsync(
            "GET", route, IdentityEndpointHost.Operator(Subject, "munich"), query: "?fabId=Not-A-Fab");

        var expected = IdentityEndpointHost.Problem(
            400, code, "None of your fab groups is a usable fab name.", BadRequestType);
        unusableGroup.Status.ShouldBe(400);
        unusableGroup.Body.ShouldBe(expected);
        unusableNamedFab.Status.ShouldBe(400);
        unusableNamedFab.Body.ShouldBe(expected);
        host.QuerySource.Reads.ShouldBe(0);
    }

    [Theory]
    [InlineData("/devices/", "Device", "device")]
    [InlineData("/kiosks/", "Kiosk", "kiosk")]
    public async Task A_list_by_a_caller_with_no_fab_membership_is_refused_before_the_store_is_read(
        string route,
        string kind,
        string prefix)
    {
        await using var host = SeededHost(kind, prefix);

        await Should.ThrowAsync<FabAuthorizationException>(
            host.SendAsync("GET", route, IdentityEndpointHost.Operator(Subject)));

        host.QuerySource.Reads.ShouldBe(0);
    }

    private static IdentityEndpointHost SeededHost(string kind, string prefix)
    {
        IdentityEndpointHost host = new();
        foreach (var fab in new[] { "munich", "dresden", "bremen" })
        {
            host.Seed(prefix + "-" + fab, ClientKind.From(kind), fab);
        }

        return host;
    }

    private static ClaimsPrincipal Caller() => IdentityEndpointHost.Operator(Subject, "dresden");

    private static ClaimsPrincipal IsSubject() =>
        It.Is<ClaimsPrincipal>(user => user.FindFirst("sub")!.Value == Subject.ToString());

    private static RegisteredClientAggregate Added(IdentityEndpointHost host) =>
        host.Clients.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IRegisteredClientRepository.Add))
            .SelectMany(invocation => invocation.Arguments.OfType<RegisteredClientAggregate>())
            .ShouldHaveSingleItem();

    private static Dictionary<string, string>? Key(string? key) =>
        key is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = key };

    private static Task<IdentityEndpointHost.HostResponse> RegisterAsync(
        IdentityEndpointHost host,
        string fab,
        string? key = null,
        string deviceType = "plc",
        string deviceIdentifier = "line-1") =>
        host.SendAsync(
            "POST",
            Register,
            Caller(),
            bodyJson: $$"""{"deviceType":"{{deviceType}}","deviceIdentifier":"{{deviceIdentifier}}"}""",
            headers: Key(key),
            query: "?fabId=" + Uri.EscapeDataString(fab));

    private static Task<IdentityEndpointHost.HostResponse> EnrollAsync(
        IdentityEndpointHost host,
        string fab,
        string? key = null,
        string name = "lobby-1") =>
        host.SendAsync(
            "POST",
            Enroll,
            Caller(),
            bodyJson: $$"""{"name":"{{name}}"}""",
            headers: Key(key),
            query: "?fabId=" + Uri.EscapeDataString(fab));
}
