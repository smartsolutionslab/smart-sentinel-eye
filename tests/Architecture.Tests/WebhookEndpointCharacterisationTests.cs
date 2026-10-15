using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Moq;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins how the webhook ingest route authenticates an anonymous caller and what it answers
/// (spec 365, #2930, FR-003 and FR-004; security-sensitive). The refusal order is the claim:
/// <c>Bearer </c> prefix (ordinal), then the integration name, then the lookup, then the revoked
/// check (logged once per window), then the integration's own fab, then the token by mode. Every
/// refusal is the same empty 401, and each case also proves what was never reached, because a
/// lookup that moves ahead of a cheaper refusal is exactly what the 401 is meant to hide.
///
/// <para>
/// The static-hash token check is not itself observable (it is a method on the aggregate), so
/// "no token check across plants" is pinned through the JWT mode, where the check is a call on
/// <see cref="IAuthenticationService"/>, and through the right token being refused when the fab
/// is wrong. Webhook registration is pinned here too: it is the one create without an
/// <c>Idempotency-Key</c>.
/// </para>
/// </summary>
public class WebhookEndpointCharacterisationTests
{
    private const string Ingest = "/events/webhook/{integrationName}";

    private const string Registry = "/webhook-integrations/";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string Payload = """{"payload":{"a":1}}""";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly string[] CallerFabs = ["dresden", "bremen"];

    [Fact]
    public async Task A_delivery_without_an_authorization_header_is_refused_without_a_lookup()
    {
        await using var host = new EventIngestionEndpointHost();
        host.Integration("line-7", "dresden", out _);

        var response = await Deliver(host, "line-7", authorization: null);

        AssertRefused(response);
        host.Integrations.VerifyNoOtherCalls();
        host.Storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("bearer {token}")]
    [InlineData("BEARER {token}")]
    [InlineData("Bearer")]
    [InlineData("Bearer{token}")]
    [InlineData("Basic {token}")]
    public async Task A_delivery_whose_authorization_lacks_the_exact_bearer_prefix_is_refused_without_a_lookup(string authorization)
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out _);

        var response = await Deliver(host, "line-7", authorization.Replace("{token}", token, StringComparison.Ordinal));

        AssertRefused(response);
        host.Integrations.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Line 7")]
    [InlineData("7-lines")]
    [InlineData("LINE-7")]
    public async Task A_delivery_naming_a_malformed_integration_is_refused_without_a_lookup(string name)
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out _);

        var response = await Deliver(host, name, "Bearer " + token);

        AssertRefused(response);
        host.Integrations.VerifyNoOtherCalls();
        host.Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_delivery_naming_an_unknown_integration_is_refused_after_one_lookup_and_before_anything_else()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await Deliver(host, "line-7", "Bearer some-token");

        AssertRefused(response);
        host.Integrations.Verify(
            integrations => integrations.GetByNameAsync(WebhookIntegrationName.From("line-7"), It.IsAny<CancellationToken>()),
            Times.Once);
        host.Integrations.VerifyNoOtherCalls();
        host.Storage.VerifyNoOtherCalls();
        host.Authentication.VerifyNoOtherCalls();
        host.Logs.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_refusal_answers_the_same_status_and_body_as_an_unknown_integration()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out var integration);
        host.Integration("line-8", "dresden", out var revoked);
        revoked.Revoke(host.Clock.Object);
        var unknown = await Deliver(host, "line-9", "Bearer " + token);

        List<EventIngestionEndpointHost.HostResponse> refusals =
        [
            await Deliver(host, "line-7", authorization: null),
            await Deliver(host, "line-7", "bearer " + token),
            await Deliver(host, "Line 7", "Bearer " + token),
            await Deliver(host, "line-8", "Bearer " + token),
            await Deliver(host, "line-7", "Bearer " + token, fab: "bremen"),
            await Deliver(host, "line-7", "Bearer " + token, fab: "Bad Fab"),
            await Deliver(host, "line-7", "Bearer wrong"),
        ];

        integration.IsRevoked.ShouldBeFalse();
        unknown.Status.ShouldBe(401);
        foreach (var refusal in refusals)
        {
            refusal.ShouldBe(unknown);
        }
    }

    [Fact]
    public async Task A_delivery_to_a_revoked_integration_is_refused_before_the_token_is_considered_and_logged()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out var integration);
        integration.Revoke(host.Clock.Object);

        var response = await Deliver(host, "line-7", "Bearer " + token);

        AssertRefused(response);
        host.Storage.VerifyNoOtherCalls();
        host.Authentication.VerifyNoOtherCalls();
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
        var logged = host.Logs.Messages.Where(message => message.Level == Microsoft.Extensions.Logging.LogLevel.Warning).ShouldHaveSingleItem();
        logged.Text.ShouldContain("revoked webhook integration 'line-7'");
        logged.Text.ShouldContain("in fab dresden");
    }

    [Fact]
    public async Task A_revoked_integration_is_logged_once_per_window_with_its_own_fab_even_when_the_delivery_names_another()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out var integration);
        integration.Revoke(host.Clock.Object);

        var first = await Deliver(host, "line-7", "Bearer " + token, fab: "bremen");
        var second = await Deliver(host, "line-7", "Bearer " + token, fab: "dresden");

        AssertRefused(first);
        AssertRefused(second);
        var logged = host.Logs.Messages.Where(message => message.Level == Microsoft.Extensions.Logging.LogLevel.Warning).ShouldHaveSingleItem();
        logged.Text.ShouldContain("in fab dresden");
        logged.Text.ShouldNotContain("bremen");
        logged.Text.ShouldNotContain(token);
    }

    [Fact]
    public async Task A_refusal_before_the_revoked_check_is_not_logged()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out var integration);
        integration.Revoke(host.Clock.Object);

        await Deliver(host, "line-7", authorization: null);
        await Deliver(host, "line-7", "bearer " + token);
        await Deliver(host, "Line 7", "Bearer " + token);

        host.Logs.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_right_static_token_for_another_plant_is_refused_before_storage_is_asked()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out _);

        var response = await Deliver(host, "line-7", "Bearer " + token, fab: "bremen");

        AssertRefused(response);
        host.Storage.VerifyNoOtherCalls();
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
    }

    [Fact]
    public async Task A_jwt_integration_is_refused_for_another_plant_without_the_token_being_authenticated()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out var integration);
        integration.MarkAsRotated(KeycloakClientIdentifier.From("webhook-line-7"), host.Clock.Object);
        host.Authenticated(Principal("webhook-line-7", "sse.events.write", "/fabs/bremen"));

        var response = await Deliver(host, "line-7", "Bearer " + token, fab: "bremen");

        AssertRefused(response);
        host.Authentication.VerifyNoOtherCalls();
        host.Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_wrong_static_token_is_refused_before_storage_is_asked()
    {
        await using var host = new EventIngestionEndpointHost();
        host.Integration("line-7", "dresden", out _);

        var response = await Deliver(host, "line-7", "Bearer wrong-token");

        AssertRefused(response);
        host.Storage.VerifyNoOtherCalls();
        host.Authentication.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_static_token_no_longer_opens_a_jwt_integration()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out var integration);
        integration.MarkAsRotated(KeycloakClientIdentifier.From("webhook-line-7"), host.Clock.Object);

        var response = await Deliver(host, "line-7", "Bearer " + token);

        AssertRefused(response);
        host.Authentication.Verify(
            authentication => authentication.AuthenticateAsync(It.IsAny<HttpContext>(), "Bearer"),
            Times.Once);
        host.Storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("webhook-line-7", "sse.events.read", "/fabs/dresden")]
    [InlineData("webhook-other", "sse.events.write", "/fabs/dresden")]
    [InlineData("webhook-line-7", "sse.events.write", "/fabs/bremen")]
    [InlineData("webhook-line-7", "sse.events.write", "dresden")]
    public async Task A_jwt_that_misses_the_scope_the_client_or_the_fab_group_is_refused_before_storage_is_asked(
        string client,
        string scope,
        string group)
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out var integration);
        integration.MarkAsRotated(KeycloakClientIdentifier.From("webhook-line-7"), host.Clock.Object);
        host.Authenticated(Principal(client, scope, group));

        var response = await Deliver(host, "line-7", "Bearer " + token);

        AssertRefused(response);
        host.Storage.VerifyNoOtherCalls();
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
    }

    [Fact]
    public async Task A_jwt_that_fails_authentication_is_refused_before_storage_is_asked()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out var integration);
        integration.MarkAsRotated(KeycloakClientIdentifier.From("webhook-line-7"), host.Clock.Object);
        host.Authentication
            .Setup(authentication => authentication.AuthenticateAsync(It.IsAny<HttpContext>(), "Bearer"))
            .ReturnsAsync(AuthenticateResult.Fail("expired"));

        var response = await Deliver(host, "line-7", "Bearer " + token);

        AssertRefused(response);
        host.Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_valid_jwt_for_the_integrations_client_scope_and_fab_is_stored()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out var integration);
        integration.MarkAsRotated(KeycloakClientIdentifier.From("webhook-line-7"), host.Clock.Object);
        host.Authenticated(Principal("webhook-line-7", "openid sse.events.write", "/fabs/dresden"));

        var response = await Deliver(host, "line-7", "Bearer " + token);

        response.Status.ShouldBe(201);
        host.Events.Verify(events => events.Add(It.Is<EventAggregate>(stored => stored.Fab.Value == "dresden")), Times.Once);
    }

    [Fact]
    public async Task A_refused_delivery_is_refused_before_storage_readiness_and_before_the_payload_is_parsed()
    {
        await using var host = new EventIngestionEndpointHost();
        host.Integration("line-7", "dresden", out _);
        host.NotProvisioned("dresden");

        var response = await Deliver(host, "line-7", "Bearer wrong", body: """{"kind":"lower"}""");

        AssertRefused(response);
        host.Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_authenticated_delivery_to_a_fab_without_storage_answers_503_for_the_integrations_fab_before_the_payload_is_parsed()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out _);
        host.NotProvisioned("dresden");

        var response = await Deliver(host, "line-7", "Bearer " + token, body: """{"kind":"lower"}""");

        response.Status.ShouldBe(503);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            503,
            "EVENT_FAB_NOT_PROVISIONED",
            "Fab 'dresden' has no event storage yet, so this event cannot be stored. It has not been lost — it was never accepted. "
            + "Retry once provisioning has run.",
            "https://tools.ietf.org/html/rfc9110#section-15.6.4"));
        host.Storage.Verify(storage => storage.IsReadyAsync(FabIdentifier.From("dresden"), It.IsAny<CancellationToken>()), Times.Once);
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
    }

    [Fact]
    public async Task A_missing_payload_is_refused_with_400_once_the_caller_is_authenticated()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out _);

        var response = await Deliver(host, "line-7", "Bearer " + token, body: "{}");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            400,
            "EVENT_INVALID_INPUT",
            "Payload is missing: the \"payload\" property was not present. (Parameter 'element')",
            BadRequestType));
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
    }

    [Fact]
    public async Task An_invalid_body_kind_is_refused_with_400_with_the_kind_message()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out _);

        var response = await Deliver(host, "line-7", "Bearer " + token, body: """{"kind":"lower","payload":{"a":1}}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            400,
            "EVENT_INVALID_INPUT",
            "value: must start with an uppercase letter and contain only letters or digits (Parameter 'value')",
            BadRequestType));
    }

    [Fact]
    public async Task A_delivery_with_the_integrations_static_token_is_stored_under_the_integrations_name_and_default_kind()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out _);

        var response = await Deliver(host, "line-7", "Bearer " + token);

        response.Status.ShouldBe(201);
        var identifier = response.Body.Trim('"');
        response.Location.ShouldBe("/events/" + identifier);
        var stored = Stored(host);
        stored.Id.Value.ToString().ShouldBe(identifier);
        stored.Fab.Value.ShouldBe("dresden");
        stored.Source.ShouldBe(Source.Webhook);
        stored.Device.Value.ShouldBe("line-7");
        stored.Kind.Value.ShouldBe("Alarm");
        stored.OccurredAt.Value.ShouldBe(EventEnvelopeNow);
        stored.Payload.Value.ShouldBe("""{"a":1}""");
        host.Storage.Verify(storage => storage.IsReadyAsync(FabIdentifier.From("dresden"), It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_kind_and_a_time_in_the_body_replace_the_defaults()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out _);

        var response = await Deliver(
            host,
            "line-7",
            "Bearer " + token,
            body: """{"kind":"Heartbeat","occurredAt":"2026-10-15T07:30:00+00:00","payload":{"b":2}}""");

        response.Status.ShouldBe(201);
        var stored = Stored(host);
        stored.Kind.Value.ShouldBe("Heartbeat");
        stored.OccurredAt.Value.ShouldBe(new DateTimeOffset(2026, 10, 15, 7, 30, 0, TimeSpan.Zero));
        stored.Payload.Value.ShouldBe("""{"b":2}""");
    }

    [Fact]
    public async Task A_delivery_that_arrives_while_every_write_slot_is_taken_answers_429_and_stores_nothing()
    {
        await using var host = new EventIngestionEndpointHost();
        var token = host.Integration("line-7", "dresden", out _);
        using var held = host.Limiter.TryAcquire();

        var response = await Deliver(host, "line-7", "Bearer " + token);

        held.Acquired.ShouldBeTrue();
        response.Status.ShouldBe(429);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            429,
            "EVENT_INGEST_BACKPRESSURE",
            "Too many events are being stored at once; please retry."));
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
    }

    [Fact]
    public async Task Register_refuses_an_invalid_name_with_the_name_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Registry, Admin("dresden"), bodyJson: RegisterBody("Line 7", "Alarm"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(WebhookRefusal(NameRefusal));
        host.Integrations.VerifyNoOtherCalls();
        host.FabGuard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_refuses_an_invalid_default_kind_with_the_kind_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Registry, Admin("dresden"), bodyJson: RegisterBody("line-7", "alarm"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(WebhookRefusal("value: must start with an uppercase letter and contain only letters or digits (Parameter 'value')"));
        host.Integrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_parses_the_name_before_the_default_kind()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Registry, Admin("dresden"), bodyJson: RegisterBody("Line 7", "alarm"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(WebhookRefusal(NameRefusal));
    }

    [Fact]
    public async Task Register_parses_the_body_before_the_fab_is_resolved()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Registry, Admin(), bodyJson: RegisterBody("Line 7", "Alarm"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(WebhookRefusal(NameRefusal));
    }

    [Fact]
    public async Task Register_refuses_a_caller_who_holds_no_fab_without_touching_the_repository()
    {
        await using var host = new EventIngestionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            host.SendAsync("POST", Registry, Admin(), bodyJson: RegisterBody("line-7", "Alarm")));

        host.Integrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_refuses_a_multi_fab_admin_who_names_no_fab()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Registry, Admin(CallerFabs), bodyJson: RegisterBody("line-7", "Alarm"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            400,
            "EVENT_FAB_REQUIRED",
            "You are assigned to more than one fab; name the one this applies to with ?fabId=.",
            BadRequestType));
        host.Integrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Registering_a_taken_name_answers_the_handlers_409()
    {
        await using var host = new EventIngestionEndpointHost();
        host.Integration("line-7", "bremen", out _);

        var response = await host.SendAsync("POST", Registry, Admin("dresden"), bodyJson: RegisterBody("line-7", "Alarm"));

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            409,
            "WEBHOOK_INTEGRATION_NAME_TAKEN",
            "A webhook integration named 'line-7' already exists.",
            "https://tools.ietf.org/html/rfc9110#section-15.5.10"));
        host.Integrations.Verify(integrations => integrations.Add(It.IsAny<WebhookIntegration>()), Times.Never);
    }

    [Fact]
    public async Task Register_answers_201_with_the_plaintext_token_whose_hash_is_what_was_stored()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Registry, Admin("dresden"), bodyJson: RegisterBody("line-7", "Alarm"));

        response.Status.ShouldBe(201);
        response.Location.ShouldBe("/webhook-integrations/line-7");
        WebhookIntegration? stored = null;
        host.Integrations.Verify(
            integrations => integrations.Add(It.Is<WebhookIntegration>(added => Capture(added, out stored))),
            Times.Once);
        stored.ShouldNotBeNull();
        stored.Name.Value.ShouldBe("line-7");
        stored.Fab.Value.ShouldBe("dresden");
        stored.DefaultKind.Value.ShouldBe("Alarm");
        var body = System.Text.Json.JsonDocument.Parse(response.Body).RootElement;
        body.EnumerateObject().Select(property => property.Name).ShouldBe(["identifier", "name", "token"]);
        body.GetProperty("identifier").GetGuid().ShouldBe(stored.Id.Value);
        body.GetProperty("name").GetString().ShouldBe("line-7");
        stored.TokenHash.Matches(body.GetProperty("token").GetString()!).ShouldBeTrue();
        host.Integrations.Verify(integrations => integrations.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.Logs.Messages.ShouldNotContain(message => message.Text.Contains(body.GetProperty("token").GetString()!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_named_fab_is_the_fab_a_webhook_integration_is_registered_in()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Registry, Admin(CallerFabs), bodyJson: RegisterBody("line-7", "Alarm"), query: "?fabId=bremen");

        response.Status.ShouldBe(201);
        host.Integrations.Verify(integrations => integrations.Add(It.Is<WebhookIntegration>(added => added.Fab.Value == "bremen")), Times.Once);
    }

    [Theory]
    [InlineData("key-1")]
    [InlineData(" ")]
    public async Task Register_never_consults_the_idempotency_store_whatever_the_header_says(string key)
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Registry, Admin("dresden"), bodyJson: RegisterBody("line-7", "Alarm"), headers: Header("Idempotency-Key", key));

        response.Status.ShouldBe(201);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    private const string NameRefusal =
        "value: must start with a lowercase letter and contain only lowercase letters, digits, or '-' (Parameter 'value')";

    private static DateTimeOffset EventEnvelopeNow => EventIngestionEndpointHost.Now;

    private static bool Capture(WebhookIntegration added, out WebhookIntegration? stored)
    {
        stored = added;

        return true;
    }

    private static void AssertRefused(EventIngestionEndpointHost.HostResponse response)
    {
        response.Status.ShouldBe(401);
        response.Body.ShouldBeEmpty();
        response.Location.ShouldBeEmpty();
    }

    private static EventAggregate Stored(EventIngestionEndpointHost host)
    {
        var added = host.Events.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IEventRepository.Add))
            .SelectMany(invocation => invocation.Arguments.OfType<EventAggregate>())
            .ShouldHaveSingleItem();
        host.Events.Verify(events => events.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);

        return added;
    }

    private static ClaimsPrincipal Principal(string client, string scope, string group) =>
        new(new ClaimsIdentity(
            [new Claim("azp", client), new Claim("scope", scope), new Claim("groups", group)],
            "test"));

    private static ClaimsPrincipal Admin(params string[] fabs) => EventIngestionEndpointHost.Operator(Subject, fabs);

    private static Dictionary<string, string> Header(string name, string value) => new() { [name] = value };

    private static string RegisterBody(string name, string defaultKind) =>
        $$"""{"name":"{{name}}","defaultKind":"{{defaultKind}}"}""";

    private static string WebhookRefusal(string detail) =>
        EventIngestionEndpointHost.Problem(400, "WEBHOOK_INVALID_INPUT", detail, BadRequestType);

    private static Task<EventIngestionEndpointHost.HostResponse> Deliver(
        EventIngestionEndpointHost host,
        string name,
        string? authorization,
        string fab = "dresden",
        string body = Payload) =>
        host.SendAsync(
            "POST",
            Ingest,
            new ClaimsPrincipal(new ClaimsIdentity()),
            route: new Dictionary<string, string> { ["integrationName"] = name },
            bodyJson: body,
            headers: authorization is null ? null : Header("Authorization", authorization),
            query: "?fabId=" + Uri.EscapeDataString(fab));
}
