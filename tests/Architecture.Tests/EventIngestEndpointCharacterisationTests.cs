using System.Security.Claims;
using Moq;
using SmartSentinelEye.EventIngestion.Api.Requests;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what <c>POST /events/manual</c> answers and in which order it refuses, and the refusals of
/// <c>GET /events</c> (spec 365, #2930, FR-003; the manual write is security-sensitive). The
/// order is the claim: key, then the revoked-webhook caller, then the fab, then storage readiness,
/// then the envelope parse, and only then the idempotent store. Each case that is refused early
/// also proves what was never reached.
///
/// <para>
/// The list's happy path is not driven here: <c>ListEventsQueryHandler</c> materialises with
/// <c>ToListAsync</c>, which needs an async query provider, and <c>ListEventsFilteringTests</c>
/// observes it in CI. Everything that is refused before the query runs is pinned.
/// </para>
/// </summary>
public class EventIngestEndpointCharacterisationTests
{
    private const string Manual = "/events/manual";

    private const string List = "/events/";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string KeyEmptyBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"IDEMPOTENCY_KEY_MALFORMED","status":400,"detail":"Idempotency-Key was present but empty."}""";

    private const string ValidBody =
        """{"deviceId":"press-4","kind":"DoorOpened","occurredAt":"2026-10-15T07:59:00+00:00","payload":{"a":1}}""";

    private const string KindRefusal = "value: must start with an uppercase letter and contain only letters or digits (Parameter 'value')";

    private const string DeviceRefusal = "value: must be alphanumeric or '.', '_', '-' and start with a letter or digit (Parameter 'value')";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly string[] CallerFabs = ["dresden", "bremen"];

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_before_the_revoked_caller_check_and_the_fab_guard()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Manual, CallerFrom("dresden", azp: "webhook-line-7"), bodyJson: ValidBody, headers: Header("Idempotency-Key", " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.Integrations.VerifyNoOtherCalls();
        host.FabGuard.VerifyNoOtherCalls();
        host.Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_before_an_invalid_body()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Manual, Caller("dresden"), bodyJson: """{"deviceId":"","kind":"x"}""", headers: Header("Idempotency-Key", " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
    }

    [Fact]
    public async Task A_caller_of_a_revoked_integrations_client_is_refused_with_401_before_the_fab_is_resolved()
    {
        await using var host = new EventIngestionEndpointHost();
        host.Integrations
            .Setup(integrations => integrations.IsRevokedByKeycloakClientIdAsync(
                KeycloakClientIdentifier.From("webhook-line-7"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await host.SendAsync("POST", Manual, CallerFrom(null, azp: "webhook-line-7"), bodyJson: ValidBody);

        response.Status.ShouldBe(401);
        response.Body.ShouldBeEmpty();
        host.FabGuard.VerifyNoOtherCalls();
        host.Storage.VerifyNoOtherCalls();
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
    }

    [Fact]
    public async Task A_caller_of_a_client_no_revoked_integration_claims_is_asked_about_once_and_proceeds()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Manual, CallerFrom("dresden", azp: "operator-console"), bodyJson: ValidBody);

        response.Status.ShouldBe(201);
        host.Integrations.Verify(
            integrations => integrations.IsRevokedByKeycloakClientIdAsync(
                KeycloakClientIdentifier.From("operator-console"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.Integrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_caller_with_no_azp_is_not_asked_about_at_all()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Manual, Caller("dresden"), bodyJson: ValidBody);

        response.Status.ShouldBe(201);
        host.Integrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_caller_whose_azp_is_longer_than_a_client_identifier_can_be_proceeds_without_a_lookup()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Manual, CallerFrom("dresden", azp: new string('c', KeycloakClientIdentifier.MaximumLength + 1)), bodyJson: ValidBody);

        response.Status.ShouldBe(201);
        host.Integrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_caller_who_holds_no_fab_is_refused_before_storage_is_asked()
    {
        await using var host = new EventIngestionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            host.SendAsync("POST", Manual, Caller(), bodyJson: ValidBody));

        host.Storage.VerifyNoOtherCalls();
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
    }

    [Fact]
    public async Task A_multi_fab_caller_who_names_no_fab_is_refused_before_storage_is_asked()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Manual, Caller(CallerFabs), bodyJson: ValidBody);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            400,
            "EVENT_FAB_REQUIRED",
            "You are assigned to more than one fab; name the one this applies to with ?fabId=.",
            BadRequestType));
        host.Storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_fab_without_storage_answers_503_for_that_fab_before_the_body_is_parsed()
    {
        await using var host = new EventIngestionEndpointHost();
        host.NotProvisioned("dresden");

        var response = await host.SendAsync("POST", Manual, Caller("dresden"), bodyJson: """{"deviceId":"","kind":"x"}""");

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
    public async Task A_missing_payload_is_refused_with_400_after_storage_was_asked()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Manual, Caller("dresden"), bodyJson: """{"deviceId":"press-4","kind":"DoorOpened","occurredAt":"2026-10-15T07:59:00+00:00"}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("Payload is missing: the \"payload\" property was not present. (Parameter 'element')"));
        host.Storage.Verify(storage => storage.IsReadyAsync(FabIdentifier.From("dresden"), It.IsAny<CancellationToken>()), Times.Once);
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_kind_is_refused_with_400_with_the_kind_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Manual, Caller("dresden"), bodyJson: """{"deviceId":"press-4","kind":"door","payload":{}}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput(KindRefusal));
    }

    [Fact]
    public async Task The_device_is_parsed_before_the_kind()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Manual, Caller("dresden"), bodyJson: """{"deviceId":"-press","kind":"door","payload":{}}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput(DeviceRefusal));
    }

    [Fact]
    public async Task A_keyed_write_consults_the_store_with_the_route_the_caller_the_fab_and_the_body()
    {
        await using var host = new EventIngestionEndpointHost();
        IngestManualEventRequest body = new(
            "press-4",
            "DoorOpened",
            new DateTimeOffset(2026, 10, 15, 7, 59, 0, TimeSpan.Zero),
            System.Text.Json.JsonDocument.Parse("""{"a":1}""").RootElement);

        var response = await host.SendAsync(
            "POST", Manual, Caller("dresden"), bodyJson: ValidBody, headers: Header("Idempotency-Key", "key-1"));

        response.Status.ShouldBe(201);
        var scope = host.BegunScope();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /events/manual");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(body));
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_named_fab_is_the_fab_in_the_scope_and_in_the_stored_event()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST",
            Manual,
            Caller(CallerFabs),
            bodyJson: ValidBody,
            headers: Header("Idempotency-Key", "key-1"),
            query: "?fabId=bremen");

        response.Status.ShouldBe(201);
        host.BegunScope().Fab.ShouldBe(Option<string>.Some("bremen"));
        Stored(host).Fab.Value.ShouldBe("bremen");
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), "bremen", It.IsAny<CancellationToken>()),
            Times.Once);
        host.Storage.Verify(storage => storage.IsReadyAsync(FabIdentifier.From("bremen"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_replayed_key_answers_the_first_events_201_and_stores_nothing()
    {
        await using var host = new EventIngestionEndpointHost();
        var first = Guid.NewGuid();
        host.IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdempotencyReservation.CompletedWith(first));

        var response = await host.SendAsync(
            "POST", Manual, Caller("dresden"), bodyJson: ValidBody, headers: Header("Idempotency-Key", "key-1"));

        response.Status.ShouldBe(201);
        response.Location.ShouldBe("/events/" + first);
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
    }

    [Fact]
    public async Task A_key_bound_to_another_request_is_refused_with_422_and_stores_nothing()
    {
        await using var host = new EventIngestionEndpointHost();
        host.IdempotencyStore
            .Setup(store => store.BeginAsync(It.IsAny<IdempotencyScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdempotencyReservation.Mismatched);

        var response = await host.SendAsync(
            "POST", Manual, Caller("dresden"), bodyJson: ValidBody, headers: Header("Idempotency-Key", "key-1"));

        response.Status.ShouldBe(422);
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
    }

    [Fact]
    public async Task An_unkeyed_write_answers_201_with_the_location_and_stores_the_envelope_as_sent_in_the_resolved_fab()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Manual, Caller("dresden"), bodyJson: ValidBody);

        response.Status.ShouldBe(201);
        var identifier = response.Body.Trim('"');
        response.Location.ShouldBe("/events/" + identifier);
        var stored = Stored(host);
        stored.Id.Value.ToString().ShouldBe(identifier);
        stored.Fab.Value.ShouldBe("dresden");
        stored.Source.ShouldBe(Source.Manual);
        stored.Device.Value.ShouldBe("press-4");
        stored.Kind.Value.ShouldBe("DoorOpened");
        stored.OccurredAt.Value.ShouldBe(new DateTimeOffset(2026, 10, 15, 7, 59, 0, TimeSpan.Zero));
        stored.Payload.Value.ShouldBe("""{"a":1}""");
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_unregistered_kind_from_a_declared_discovery_source_is_held_and_answers_202_without_storing()
    {
        await using var host = new EventIngestionEndpointHost();
        host.AdmissionSource
            .Setup(source => source.DeclaredSourceModesAsync(
                It.IsAny<IReadOnlyCollection<FabIdentifier>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(FabIdentifier Fab, Source Source), EventTypeMode>
            {
                [(FabIdentifier.From("dresden"), Source.Manual)] = EventTypeMode.Discovery,
            });
        host.AdmissionSource
            .Setup(source => source.RegisteredKindsAsync(
                FabIdentifier.From("dresden"),
                It.IsAny<IReadOnlyCollection<Kind>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Kind>());

        var response = await host.SendAsync("POST", Manual, Caller("dresden"), bodyJson: ValidBody);

        response.Status.ShouldBe(202);
        response.Body.ShouldBeEmpty();
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
        host.DeadLetters.Verify(deadLetters => deadLetters.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_write_that_arrives_while_every_write_slot_is_taken_answers_429_and_stores_nothing()
    {
        await using var host = new EventIngestionEndpointHost();
        using var held = host.Limiter.TryAcquire();

        var response = await host.SendAsync("POST", Manual, Caller("dresden"), bodyJson: ValidBody);

        held.Acquired.ShouldBeTrue();
        response.Status.ShouldBe(429);
        host.Events.Verify(events => events.Add(It.IsAny<EventAggregate>()), Times.Never);
    }

    [Fact]
    public async Task List_refuses_an_unknown_source_with_the_source_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("GET", List, Caller("dresden"), query: "?source=robot");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(ListRefusal("Unknown event Source 'robot'. Expected: plc | inference | manual | webhook. (Parameter 'value')"));
        host.EventQuery.VerifyNoOtherCalls();
        host.FabGuard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_refuses_an_invalid_device_with_the_device_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("GET", List, Caller("dresden"), query: "?deviceId=-press");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(ListRefusal(DeviceRefusal));
        host.EventQuery.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_refuses_an_invalid_kind_with_the_kind_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("GET", List, Caller("dresden"), query: "?kind=door");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(ListRefusal(KindRefusal));
        host.EventQuery.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_parses_the_source_before_the_device_and_the_device_before_the_kind()
    {
        await using var host = new EventIngestionEndpointHost();

        var sourceAndKind = await host.SendAsync("GET", List, Caller("dresden"), query: "?source=robot&kind=door");
        var deviceAndKind = await host.SendAsync("GET", List, Caller("dresden"), query: "?deviceId=-press&kind=door");
        var all = await host.SendAsync("GET", List, Caller("dresden"), query: "?source=robot&deviceId=-press&kind=door");

        sourceAndKind.Body.ShouldBe(ListRefusal("Unknown event Source 'robot'. Expected: plc | inference | manual | webhook. (Parameter 'value')"));
        deviceAndKind.Body.ShouldBe(ListRefusal(DeviceRefusal));
        all.Body.ShouldBe(sourceAndKind.Body);
    }

    [Fact]
    public async Task List_parses_the_filters_before_the_fab_is_resolved()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("GET", List, Caller(), query: "?source=robot&fabId=bremen");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(ListRefusal("Unknown event Source 'robot'. Expected: plc | inference | manual | webhook. (Parameter 'value')"));
        host.FabGuard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_refuses_a_caller_who_holds_no_fab_without_running_the_query()
    {
        await using var host = new EventIngestionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(host.SendAsync("GET", List, Caller()));

        host.EventQuery.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_refuses_a_caller_whose_groups_hold_no_usable_fab_name()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("GET", List, Caller("x"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            400,
            "EVENT_FAB_REQUIRED",
            "None of your fab groups is a usable fab name.",
            BadRequestType));
        host.EventQuery.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_resolves_the_named_fab_before_the_handler_refuses_a_page_size_out_of_range()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("GET", List, Caller(CallerFabs), query: "?fabId=bremen&pageSize=1001");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            400,
            "EVENT_LIST_PAGE_SIZE_OUT_OF_RANGE",
            "pageSize 1001 must be between 1 and 1000.",
            BadRequestType));
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), "bremen", It.IsAny<CancellationToken>()),
            Times.Once);
        host.EventQuery.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_refuses_a_malformed_cursor_naming_it()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("GET", List, Caller("dresden"), query: "?cursor=not-a-cursor");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            400,
            "EVENT_LIST_INVALID_CURSOR",
            "Cursor 'not-a-cursor' is not a valid pagination cursor.",
            BadRequestType));
        host.EventQuery.VerifyNoOtherCalls();
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

    private static ClaimsPrincipal CallerFrom(string? fab, string? azp) =>
        EventIngestionEndpointHost.OperatorFrom(Subject, azp, fab is null ? [] : [fab]);

    private static ClaimsPrincipal Caller(params string[] fabs) => EventIngestionEndpointHost.Operator(Subject, fabs);

    private static Dictionary<string, string> Header(string name, string value) => new() { [name] = value };

    private static string InvalidInput(string detail) =>
        EventIngestionEndpointHost.Problem(400, "EVENT_INVALID_INPUT", detail, BadRequestType);

    private static string ListRefusal(string detail) =>
        EventIngestionEndpointHost.Problem(400, "EVENT_LIST_INVALID_INPUT", detail, BadRequestType);
}
