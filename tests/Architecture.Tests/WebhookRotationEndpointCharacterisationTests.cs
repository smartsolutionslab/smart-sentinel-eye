using System.Security.Claims;
using Moq;
using SmartSentinelEye.Identity.Api.Requests;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what <c>POST /webhook-integrations/{name}/rotate</c> answers, in which order it refuses,
/// what it hands the idempotency store and what a replay returns (spec 367, #2932, FR-003 and
/// FR-004), so that splitting <c>Rotate</c> under ADR-0084's thirty lines cannot move a guard,
/// change the scope's fingerprint or let a retry rotate a secret a second time. Security-
/// sensitive: rotation mints or rolls a Keycloak client secret and returns it, and a duplicate
/// invalidates the secret the first attempt already delivered.
///
/// <para>
/// The real rotate and replay handlers run; only the repository, the Keycloak admin port, the
/// integration-status lookup, the event bus, the commit, the inner disable handler, the fab guard
/// and the idempotency store behind them are doubles. The two fingerprints a keyed rotation is
/// reserved under are compared with <see cref="IdempotencyFingerprint.Of{TRequest}"/> computed
/// here from the body, the route name and the literal precondition text, so a transposed or
/// dropped input changes the hash and fails. Problem bodies are compared as the exact JSON. A
/// caller the fab guard refuses surfaces as <see cref="FabAuthorizationException"/> thrown out of
/// the delegate: the global exception handler maps it to 403 in the running service, and an
/// in-process drive does not include that handler, so the throw is what is pinned.
/// </para>
/// </summary>
public class WebhookRotationEndpointCharacterisationTests
{
    private const string Rotate = "/webhook-integrations/{name}/rotate";

    private const string List = "/webhook-integrations/";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private const string PreconditionFailedType = "https://tools.ietf.org/html/rfc9110#section-15.5.13";

    private const string UnusableFab = "Not A Fab";

    private const string FabRefusal = "value: must be lowercase letters, digits, or '-' and start with a letter (Parameter 'value')";

    private const string KeyEmptyBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"IDEMPOTENCY_KEY_MALFORMED","status":400,"detail":"Idempotency-Key was present but empty."}""";

    private const string PreconditionRequiredBody =
        """{"title":"IF_MATCH_REQUIRED","status":428,"detail":"This request must be conditional. Send If-Match with the version the resource was read at, or If-None-Match: * if you are creating it."}""";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly Guid Resource = new("0190a0f0-0000-7000-8000-0000000000aa");

    private static readonly RotateWebhookClientRequest Body = new("dresden");

    [Fact]
    public async Task Rotate_reads_a_malformed_idempotency_key_before_it_parses_the_fab_or_asks_the_guard()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RotateAsync(host, UnusableFab, Headers(key: " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.FabGuard.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
        host.Clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rotate_reads_a_malformed_idempotency_key_before_the_upsert_precondition()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RotateAsync(host, "dresden", Headers(key: " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.FabGuard.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rotate_refuses_an_unusable_body_fab_with_the_fab_message_before_the_guard_is_asked()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RotateAsync(host, UnusableFab, Headers(ifNoneMatch: "*"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(400, "WEBHOOK_INVALID_INPUT", FabRefusal, BadRequestType));
        host.FabGuard.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rotate_runs_the_fab_guard_before_it_reads_the_upsert_precondition()
    {
        await using var host = new IdentityEndpointHost();
        host.GuardRefuses("dresden");

        var refusal = await Should.ThrowAsync<FabAuthorizationException>(
            RotateAsync(host, "dresden", Headers()));

        refusal.FabId.ShouldBe("dresden");
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
        host.Clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rotate_without_either_precondition_header_answers_428_after_the_guard_and_before_the_store()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RotateAsync(host, "dresden", Headers(key: "key-1"));

        response.Status.ShouldBe(428);
        response.Body.ShouldBe(PreconditionRequiredBody);
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(IsSubject(), "dresden", It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
        host.Clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rotate_with_both_precondition_headers_answers_400_before_the_store()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RotateAsync(host, "dresden", Headers(key: "key-1", ifMatch: "\"3\"", ifNoneMatch: "*"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            400,
            "IF_MATCH_MALFORMED",
            "Send either If-Match or If-None-Match, not both.",
            BadRequestType));
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(IsSubject(), "dresden", It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rotate_with_an_if_none_match_that_is_not_the_wildcard_answers_400_before_the_store()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RotateAsync(host, "dresden", Headers(key: "key-1", ifNoneMatch: "\"1\""));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            400,
            "IF_MATCH_MALFORMED",
            "If-None-Match must be '*' here; it asserts the resource does not exist yet.",
            BadRequestType));
        host.IdempotencyStore.VerifyNoOtherCalls();
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_keyed_creating_rotation_reserves_the_route_the_caller_the_fab_and_a_fingerprint_over_the_body_the_name_and_the_wildcard()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RotateAsync(host, "dresden", Headers(key: "key-1", ifNoneMatch: "*"));

        response.Status.ShouldBe(200);
        var scope = host.BegunScope();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /webhook-integrations/{name}/rotate");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(Body, "line-7", "*"));
        scope.Fingerprint.ShouldNotBe(IdempotencyFingerprint.Of(Body, "line-7", "3"));
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(
                It.Is<IdempotencyClaim>(claim => claim.Scope.Key.Value == "key-1"),
                Added(host).Id.Value,
                AggregateVersion.From(0),
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.Verify(
            store => store.ReleaseAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_keyed_rotation_at_a_version_reserves_a_fingerprint_over_the_body_the_name_and_that_version()
    {
        await using var host = new IdentityEndpointHost();
        HoldWebhookClient(host, version: 3);

        var response = await RotateAsync(host, "dresden", Headers(key: "key-1", ifMatch: "\"3\""));

        response.Status.ShouldBe(200);
        var scope = host.BegunScope();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /webhook-integrations/{name}/rotate");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(Body, "line-7", "3"));
        scope.Fingerprint.ShouldNotBe(IdempotencyFingerprint.Of(Body, "line-7", "*"));
    }

    [Fact]
    public async Task An_unkeyed_creating_rotation_answers_200_with_the_new_credentials_and_never_touches_the_store()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RotateAsync(host, "dresden", Headers(ifNoneMatch: "*"));

        var added = Added(host);
        response.Status.ShouldBe(200);
        response.Body.ShouldBe(
            $$"""{"registeredClientIdentifier":"{{added.Id.Value}}","version":0,"clientId":"webhook-line-7","integrationName":"line-7","fab":"dresden","clientSecret":"{{IdentityEndpointHost.CreatedSecret}}"}""");
        added.ClientId.Value.ShouldBe("webhook-line-7");
        added.Kind.ShouldBe(ClientKind.WebhookIntegration);
        added.Fab.Value.ShouldBe("dresden");
        added.Registration.By.Value.ShouldBe(Subject);
        host.Keycloak.Verify(
            keycloak => keycloak.CreateClientAsync(
                It.Is<KeycloakClientRepresentation>(representation =>
                    representation.ClientId == "webhook-line-7"
                    && representation.Attributes["sse.kind"] == "webhook"
                    && representation.Attributes["sse.integrationName"] == "line-7"
                    && representation.Attributes["sse.fab"] == "dresden"),
                "/fabs/dresden",
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.Keycloak.Verify(
            keycloak => keycloak.RotateClientSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Events.Verify(
            events => events.PublishAsync(It.IsAny<WebhookIntegrationRotatedV1>(), It.IsAny<CancellationToken>()),
            Times.Once);
        host.Commit.Verify(commit => commit.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_unkeyed_rotation_at_a_version_rolls_the_secret_and_answers_200_with_the_rolled_credentials()
    {
        await using var host = new IdentityEndpointHost();
        var held = HoldWebhookClient(host, version: 3);

        var response = await RotateAsync(host, "dresden", Headers(ifMatch: "\"3\""));

        response.Status.ShouldBe(200);
        response.Body.ShouldBe(
            $$"""{"registeredClientIdentifier":"{{held.Id.Value}}","version":3,"clientId":"webhook-line-7","integrationName":"line-7","fab":"dresden","clientSecret":"{{IdentityEndpointHost.RotatedSecret}}"}""");
        host.Keycloak.Verify(
            keycloak => keycloak.RotateClientSecretAsync("webhook-line-7", It.IsAny<CancellationToken>()),
            Times.Once);
        host.Keycloak.Verify(
            keycloak => keycloak.CreateClientAsync(
                It.IsAny<KeycloakClientRepresentation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_keyed_rotation_the_handler_refuses_answers_the_handlers_problem_and_releases_the_key()
    {
        await using var host = new IdentityEndpointHost();

        var response = await RotateAsync(host, "dresden", Headers(key: "key-1", ifMatch: "\"3\""));

        response.Status.ShouldBe(412);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            412,
            "WEBHOOK_CLIENT_NOT_FOUND",
            "No webhook client 'webhook-line-7' exists to be at version 3. Send If-None-Match: * to create it.",
            PreconditionFailedType));
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
    public async Task A_replayed_rotation_with_a_stale_if_match_returns_the_stored_secret_without_rotating_again()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource);
        host.Held(
            RegisteredClientIdentifier.From(Resource), "webhook-line-9", ClientKind.WebhookIntegration, "dresden", 5);

        var response = await RotateAsync(host, "dresden", Headers(key: "key-1", ifMatch: "\"3\""));

        response.Status.ShouldBe(200);
        response.Body.ShouldBe(
            $$"""{"registeredClientIdentifier":"{{Resource}}","version":5,"clientId":"webhook-line-9","integrationName":"line-7","fab":"dresden","clientSecret":"{{IdentityEndpointHost.ReplayedSecret}}"}""");
        host.Keycloak.Verify(
            keycloak => keycloak.ReadClientSecretAsync("webhook-line-9", It.IsAny<CancellationToken>()),
            Times.Once);
        host.Keycloak.Verify(
            keycloak => keycloak.RotateClientSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Keycloak.Verify(
            keycloak => keycloak.CreateClientAsync(
                It.IsAny<KeycloakClientRepresentation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Clients.Verify(clients => clients.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
        host.Events.VerifyNoOtherCalls();
        host.Commit.VerifyNoOtherCalls();
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_replayed_creating_rotation_returns_the_stored_secret_without_creating_a_client_again()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource);
        host.Held(
            RegisteredClientIdentifier.From(Resource), "webhook-line-7", ClientKind.WebhookIntegration, "dresden", 0);

        var response = await RotateAsync(host, "dresden", Headers(key: "key-1", ifNoneMatch: "*"));

        response.Status.ShouldBe(200);
        response.Body.ShouldBe(
            $$"""{"registeredClientIdentifier":"{{Resource}}","version":0,"clientId":"webhook-line-7","integrationName":"line-7","fab":"dresden","clientSecret":"{{IdentityEndpointHost.ReplayedSecret}}"}""");
        host.Keycloak.Verify(
            keycloak => keycloak.CreateClientAsync(
                It.IsAny<KeycloakClientRepresentation>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Keycloak.Verify(
            keycloak => keycloak.RotateClientSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        host.Clients.Verify(clients => clients.Add(It.IsAny<RegisteredClientAggregate>()), Times.Never);
    }

    [Fact]
    public async Task A_replayed_rotation_whose_registration_is_gone_answers_the_replay_handlers_problem()
    {
        await using var host = new IdentityEndpointHost();
        host.StoreReplaysAs(Resource);

        var response = await RotateAsync(host, "dresden", Headers(key: "key-1", ifMatch: "\"3\""));

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(IdentityEndpointHost.Problem(
            409,
            "REPLAYED_REGISTRATION_MISSING",
            $"The registration '{Resource}' recorded against this Idempotency-Key no longer exists.",
            ConflictType));
        host.Keycloak.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_list_without_a_fab_spans_every_fab_the_caller_holds_and_never_asks_the_guard()
    {
        await using var host = SeededHost();

        var response = await host.SendAsync("GET", List, IdentityEndpointHost.Operator(Subject, "munich", "dresden"));

        response.Status.ShouldBe(200);
        IdentityEndpointHost.ClientIds(response).ShouldBe(["webhook-munich", "webhook-dresden"], ignoreOrder: true);
        host.FabGuard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_list_naming_a_held_fab_asks_the_guard_once_and_narrows_to_that_fab()
    {
        await using var host = SeededHost();

        var response = await host.SendAsync(
            "GET", List, IdentityEndpointHost.Operator(Subject, "munich", "dresden"), query: "?fabId=munich");

        response.Status.ShouldBe(200);
        IdentityEndpointHost.ClientIds(response).ShouldBe(["webhook-munich"]);
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(IsSubject(), "munich", It.IsAny<CancellationToken>()),
            Times.Once);
        host.FabGuard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_list_naming_a_fab_the_caller_does_not_hold_lets_the_guards_refusal_escape_before_the_store_is_read()
    {
        await using var host = SeededHost();
        host.GuardRefuses("bremen");

        var refusal = await Should.ThrowAsync<FabAuthorizationException>(
            host.SendAsync("GET", List, IdentityEndpointHost.Operator(Subject, "munich"), query: "?fabId=bremen"));

        refusal.FabId.ShouldBe("bremen");
        host.QuerySource.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task A_list_skips_a_group_that_is_not_a_usable_fab_and_lists_the_fabs_that_are()
    {
        await using var host = SeededHost();

        var response = await host.SendAsync(
            "GET", List, IdentityEndpointHost.Operator(Subject, "Not-A-Fab", "munich"));

        response.Status.ShouldBe(200);
        IdentityEndpointHost.ClientIds(response).ShouldBe(["webhook-munich"]);
    }

    [Fact]
    public async Task A_list_with_no_usable_fab_answers_400_with_the_routes_own_code_and_never_reads_the_store()
    {
        await using var host = SeededHost();

        var unusableGroup = await host.SendAsync("GET", List, IdentityEndpointHost.Operator(Subject, "Not-A-Fab"));
        // Mock guard admits any fab, so 400 here; production answers 403 unless the token carries /fabs/Not-A-Fab.
        var unusableNamedFab = await host.SendAsync(
            "GET", List, IdentityEndpointHost.Operator(Subject, "munich"), query: "?fabId=Not-A-Fab");

        var expected = IdentityEndpointHost.Problem(
            400, "WEBHOOK_INVALID_INPUT", "None of your fab groups is a usable fab name.", BadRequestType);
        unusableGroup.Status.ShouldBe(400);
        unusableGroup.Body.ShouldBe(expected);
        unusableNamedFab.Status.ShouldBe(400);
        unusableNamedFab.Body.ShouldBe(expected);
        host.QuerySource.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task A_list_by_a_caller_with_no_fab_membership_is_refused_before_the_store_is_read()
    {
        await using var host = SeededHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            host.SendAsync("GET", List, IdentityEndpointHost.Operator(Subject)));

        host.QuerySource.Reads.ShouldBe(0);
    }

    private static IdentityEndpointHost SeededHost()
    {
        IdentityEndpointHost host = new();
        foreach (var fab in new[] { "munich", "dresden", "bremen" })
        {
            host.Seed("webhook-" + fab, ClientKind.WebhookIntegration, fab);
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

    private static RegisteredClientAggregate HoldWebhookClient(IdentityEndpointHost host, int version)
    {
        var held = host.Held(
            RegisteredClientIdentifier.From(Guid.NewGuid()),
            "webhook-line-7",
            ClientKind.WebhookIntegration,
            "dresden",
            version);
        host.Clients
            .Setup(clients => clients.GetWithinFabAsync(
                FabIdentifier.From("dresden"), ClientId.From("webhook-line-7"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<RegisteredClientAggregate>.Some(held));

        return held;
    }

    private static Dictionary<string, string> Headers(
        string? key = null,
        string? ifMatch = null,
        string? ifNoneMatch = null)
    {
        Dictionary<string, string> headers = [];
        if (key is not null)
        {
            headers["Idempotency-Key"] = key;
        }

        if (ifMatch is not null)
        {
            headers["If-Match"] = ifMatch;
        }

        if (ifNoneMatch is not null)
        {
            headers["If-None-Match"] = ifNoneMatch;
        }

        return headers;
    }

    private static Task<IdentityEndpointHost.HostResponse> RotateAsync(
        IdentityEndpointHost host,
        string fab,
        Dictionary<string, string> headers) =>
        host.SendAsync(
            "POST",
            Rotate,
            Caller(),
            route: new Dictionary<string, string> { ["name"] = "line-7" },
            bodyJson: $$"""{"fabId":"{{fab}}"}""",
            headers: headers);
}
