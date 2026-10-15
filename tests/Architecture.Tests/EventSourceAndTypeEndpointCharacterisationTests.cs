using System.Security.Claims;
using Moq;
using SmartSentinelEye.EventIngestion.Api.Requests;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what the EventIngestion source-mode and event-type write endpoints answer, and in which
/// order they refuse (spec 365, #2930, FR-003), so that splitting <c>Declare</c>, <c>Change</c>
/// and <c>Register</c> under ADR-0084's thirty lines cannot change a status, a problem body, or
/// which refusal wins when a request is wrong in two ways.
///
/// <para>
/// The real command handlers run; only the repositories behind them are doubles, so an argument
/// transposed inside a moved lambda changes what the repository is asked for, or what the
/// idempotency store is asked to reserve, and fails here. Problem bodies are compared as the
/// exact JSON.
/// </para>
///
/// <para>
/// A caller who holds no fab is refused by <see cref="FabAuthorizationException"/> thrown out of
/// the delegate. The global exception handler maps it to 403 in the running service, and an
/// in-process drive does not include that handler, so the throw is what is pinned.
/// </para>
/// </summary>
public class EventSourceAndTypeEndpointCharacterisationTests
{
    private const string Sources = "/event-sources/";

    private const string SourceModeRoute = "/event-sources/{source}/mode";

    private const string Types = "/event-types/";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private const string SourceRefusal = "Unknown event Source 'robot'. Expected: plc | inference | manual | webhook. (Parameter 'value')";

    private const string KindRefusal = "value: must start with an uppercase letter and contain only letters or digits (Parameter 'value')";

    private const string KeyEmptyBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"IDEMPOTENCY_KEY_MALFORMED","status":400,"detail":"Idempotency-Key was present but empty."}""";

    private const string PreconditionRequired =
        """{"title":"IF_MATCH_REQUIRED","status":428,"detail":"This request must be conditional. Send If-Match with the version the resource was read at."}""";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly string[] CallerFabs = ["dresden", "bremen"];

    [Fact]
    public async Task Declare_refuses_an_unknown_source_with_the_source_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Sources, Caller("dresden"), bodyJson: DeclareBody("robot", "strict"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(SourceModeRefusal(SourceRefusal));
        host.SourceModes.VerifyNoOtherCalls();
        host.FabGuard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Declare_refuses_an_unknown_mode_with_the_mode_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Sources, Caller("dresden"), bodyJson: DeclareBody("plc", "lenient"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(SourceModeRefusal("Unknown event-type mode 'lenient'. (Parameter 'value')"));
        host.SourceModes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Declare_parses_the_source_before_the_mode()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Sources, Caller("dresden"), bodyJson: DeclareBody("robot", "lenient"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(SourceModeRefusal(SourceRefusal));
    }

    [Fact]
    public async Task Declare_parses_the_body_before_it_reads_the_idempotency_key()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Sources, Caller("dresden"), bodyJson: DeclareBody("robot", "strict"), headers: Header("Idempotency-Key", " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(SourceModeRefusal(SourceRefusal));
    }

    [Fact]
    public async Task Declare_refuses_a_malformed_idempotency_key_with_the_key_message_before_the_fab_is_resolved()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Sources, Caller(), bodyJson: DeclareBody("plc", "strict"), headers: Header("Idempotency-Key", " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.SourceModes.VerifyNoOtherCalls();
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Declare_refuses_a_caller_who_holds_no_fab_without_touching_the_repository()
    {
        await using var host = new EventIngestionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            host.SendAsync("POST", Sources, Caller(), bodyJson: DeclareBody("plc", "strict")));

        host.SourceModes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Declare_refuses_a_multi_fab_caller_who_names_no_fab()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Sources, Caller(CallerFabs), bodyJson: DeclareBody("plc", "strict"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(AmbiguousFab);
        host.SourceModes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_keyed_declare_consults_the_store_with_the_route_the_caller_the_fab_and_the_body()
    {
        await using var host = new EventIngestionEndpointHost();
        DeclareSourceModeRequest body = new("plc", "strict");

        var response = await host.SendAsync(
            "POST", Sources, Caller("dresden"), bodyJson: DeclareBody("plc", "strict"), headers: Header("Idempotency-Key", "key-1"));

        response.Status.ShouldBe(201);
        var scope = host.BegunScope();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /event-sources");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(body));
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_unkeyed_declare_answers_201_with_the_source_as_location_and_never_touches_the_store()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Sources, Caller("dresden"), bodyJson: DeclareBody("inference", "discovery"));

        response.Status.ShouldBe(201);
        var identifier = response.Body.Trim('"');
        Guid.TryParse(identifier, out _).ShouldBeTrue();
        response.Location.ShouldBe("/event-sources/inference");
        host.SourceModes.Verify(
            sourceModes => sourceModes.GetAsync(FabIdentifier.From("dresden"), Source.Inference, It.IsAny<CancellationToken>()),
            Times.Once);
        host.SourceModes.Verify(
            sourceModes => sourceModes.Add(It.Is<SourceMode>(declared =>
                declared.Id.Value.ToString() == identifier
                && declared.Fab.Value == "dresden"
                && declared.Source == Source.Inference
                && declared.Mode == EventTypeMode.Discovery
                && declared.Declaration.DeclaredBy.Value == Subject)),
            Times.Once);
        host.SourceModes.Verify(sourceModes => sourceModes.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_named_fab_is_the_fab_a_source_mode_is_declared_in()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Sources, Caller(CallerFabs), bodyJson: DeclareBody("plc", "strict"), query: "?fabId=bremen");

        response.Status.ShouldBe(201);
        host.SourceModes.Verify(sourceModes => sourceModes.Add(It.Is<SourceMode>(declared => declared.Fab.Value == "bremen")), Times.Once);
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), "bremen", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Declaring_an_already_declared_pair_answers_the_handlers_409()
    {
        await using var host = new EventIngestionEndpointHost();
        host.SourceModes
            .Setup(sourceModes => sourceModes.GetAsync(It.IsAny<FabIdentifier>(), Source.Plc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<SourceMode>.Some(Declared(host, Source.Plc, EventTypeMode.Strict)));

        var response = await host.SendAsync("POST", Sources, Caller("dresden"), bodyJson: DeclareBody("plc", "discovery"));

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            409,
            "SOURCE_MODE_ALREADY_DECLARED",
            "A mode is already declared for source 'plc' in fab 'dresden'.",
            ConflictType));
        host.SourceModes.Verify(sourceModes => sourceModes.Add(It.IsAny<SourceMode>()), Times.Never);
    }

    [Fact]
    public async Task Change_without_if_match_is_refused_with_428_before_the_source_is_parsed()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await Change(host, "robot", """{"mode":"strict"}""", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(428);
        response.Body.ShouldBe(PreconditionRequired);
        host.SourceModes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Change_refuses_an_unknown_source_with_the_source_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await Change(host, "robot", """{"mode":"strict"}""", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(SourceModeRefusal(SourceRefusal));
        host.SourceModes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Change_refuses_an_unknown_mode_with_the_mode_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await Change(host, "plc", """{"mode":"lenient"}""", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(SourceModeRefusal("Unknown event-type mode 'lenient'. (Parameter 'value')"));
    }

    [Fact]
    public async Task Change_parses_the_source_before_the_mode()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await Change(host, "robot", """{"mode":"lenient"}""", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(SourceModeRefusal(SourceRefusal));
    }

    [Fact]
    public async Task Change_parses_the_route_value_before_the_fab_is_resolved()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await Change(host, "robot", """{"mode":"strict"}""", Caller(), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(SourceModeRefusal(SourceRefusal));
    }

    [Fact]
    public async Task Change_refuses_a_caller_who_holds_no_fab_without_touching_the_repository()
    {
        await using var host = new EventIngestionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            Change(host, "plc", """{"mode":"strict"}""", Caller(), ifMatch: "0"));

        host.SourceModes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Changing_an_undeclared_pair_is_the_handlers_404_for_the_resolved_fab_and_source()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await Change(host, "plc", """{"mode":"strict"}""", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(404);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            404,
            "SOURCE_MODE_NOT_DECLARED",
            "No mode is declared for source 'plc' in fab 'dresden'.",
            "https://tools.ietf.org/html/rfc9110#section-15.5.5"));
        host.SourceModes.Verify(
            sourceModes => sourceModes.GetAsync(FabIdentifier.From("dresden"), Source.Plc, It.IsAny<CancellationToken>()),
            Times.Once);
        host.SourceModes.Verify(sourceModes => sourceModes.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Changing_with_a_stale_version_is_the_handlers_409_naming_both_versions()
    {
        await using var host = new EventIngestionEndpointHost();
        var declared = Declared(host, Source.Plc, EventTypeMode.Strict);
        host.SourceModes
            .Setup(sourceModes => sourceModes.GetAsync(FabIdentifier.From("dresden"), Source.Plc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<SourceMode>.Some(declared));

        var response = await Change(host, "plc", """{"mode":"discovery"}""", Caller("dresden"), ifMatch: "7");

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(EventIngestionEndpointHost.Problem(
            409,
            "SOURCE_MODE_STALE",
            $"Source mode 'plc' has changed since version 7 (now {declared.Version}). Re-read it and reapply the change.",
            ConflictType));
        declared.Mode.ShouldBe(EventTypeMode.Strict);
    }

    [Fact]
    public async Task Changing_a_declared_pair_at_its_version_answers_200_with_the_identifier_and_stores_the_new_mode()
    {
        await using var host = new EventIngestionEndpointHost();
        var declared = Declared(host, Source.Plc, EventTypeMode.Strict);
        host.SourceModes
            .Setup(sourceModes => sourceModes.GetAsync(FabIdentifier.From("bremen"), Source.Plc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<SourceMode>.Some(declared));

        var response = await Change(
            host, "plc", """{"mode":"discovery"}""", Caller(CallerFabs), ifMatch: declared.Version.ToString(), query: "?fabId=bremen");

        response.Status.ShouldBe(200);
        response.Body.ShouldBe($"\"{declared.Id.Value}\"");
        declared.Mode.ShouldBe(EventTypeMode.Discovery);
        host.SourceModes.Verify(sourceModes => sourceModes.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), "bremen", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Register_refuses_an_invalid_kind_with_the_kind_message()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Types, Caller("dresden"), bodyJson: """{"kind":"door"}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EventTypeRefusal(KindRefusal));
        host.EventTypes.VerifyNoOtherCalls();
        host.FabGuard.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_parses_the_body_before_it_reads_the_idempotency_key()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Types, Caller("dresden"), bodyJson: """{"kind":"door"}""", headers: Header("Idempotency-Key", " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EventTypeRefusal(KindRefusal));
    }

    [Fact]
    public async Task Register_refuses_a_malformed_idempotency_key_with_the_key_message_before_the_fab_is_resolved()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Types, Caller(), bodyJson: """{"kind":"DoorOpened"}""", headers: Header("Idempotency-Key", " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.EventTypes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_refuses_a_caller_who_holds_no_fab_without_touching_the_repository()
    {
        await using var host = new EventIngestionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            host.SendAsync("POST", Types, Caller(), bodyJson: """{"kind":"DoorOpened"}"""));

        host.EventTypes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_refuses_a_multi_fab_caller_who_names_no_fab()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Types, Caller(CallerFabs), bodyJson: """{"kind":"DoorOpened"}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(AmbiguousFab);
        host.EventTypes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_keyed_register_consults_the_store_with_the_route_the_caller_the_fab_and_the_body()
    {
        await using var host = new EventIngestionEndpointHost();
        RegisterEventTypeRequest body = new("DoorOpened");

        var response = await host.SendAsync(
            "POST", Types, Caller("dresden"), bodyJson: """{"kind":"DoorOpened"}""", headers: Header("Idempotency-Key", "key-1"));

        response.Status.ShouldBe(201);
        var scope = host.BegunScope();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /event-types");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(body));
    }

    [Fact]
    public async Task An_unkeyed_register_answers_201_with_the_kind_as_location_and_never_touches_the_store()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync("POST", Types, Caller("dresden"), bodyJson: """{"kind":"DoorOpened"}""");

        response.Status.ShouldBe(201);
        var identifier = response.Body.Trim('"');
        Guid.TryParse(identifier, out _).ShouldBeTrue();
        response.Location.ShouldBe("/event-types/DoorOpened");
        host.EventTypes.Verify(
            eventTypes => eventTypes.GetRegisteredAsync(FabIdentifier.From("dresden"), Kind.From("DoorOpened"), It.IsAny<CancellationToken>()),
            Times.Once);
        host.EventTypes.Verify(
            eventTypes => eventTypes.Add(It.Is<RegisteredEventType>(registered =>
                registered.Id.Value.ToString() == identifier
                && registered.Fab.Value == "dresden"
                && registered.Kind.Value == "DoorOpened"
                && registered.Registration.RegisteredBy.Value == Subject)),
            Times.Once);
        host.DeadLetters.Verify(
            deadLetters => deadLetters.PromoteHeldAsync(FabIdentifier.From("dresden"), Kind.From("DoorOpened"), It.IsAny<CancellationToken>()),
            Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_named_fab_is_the_fab_an_event_type_is_registered_in()
    {
        await using var host = new EventIngestionEndpointHost();

        var response = await host.SendAsync(
            "POST", Types, Caller(CallerFabs), bodyJson: """{"kind":"DoorOpened"}""", query: "?fabId=bremen");

        response.Status.ShouldBe(201);
        host.EventTypes.Verify(
            eventTypes => eventTypes.Add(It.Is<RegisteredEventType>(registered => registered.Fab.Value == "bremen")),
            Times.Once);
    }

    private static string AmbiguousFab { get; } = EventIngestionEndpointHost.Problem(
        400,
        "EVENT_FAB_REQUIRED",
        "You are assigned to more than one fab; name the one this applies to with ?fabId=.",
        BadRequestType);

    private static ClaimsPrincipal Caller(params string[] fabs) => EventIngestionEndpointHost.Operator(Subject, fabs);

    private static Dictionary<string, string> Header(string name, string value) => new() { [name] = value };

    private static string DeclareBody(string source, string mode) => $$"""{"source":"{{source}}","mode":"{{mode}}"}""";

    private static Task<EventIngestionEndpointHost.HostResponse> Change(
        EventIngestionEndpointHost host,
        string source,
        string body,
        ClaimsPrincipal user,
        string? ifMatch,
        string? query = null) =>
        host.SendAsync(
            "PUT",
            SourceModeRoute,
            user,
            route: new Dictionary<string, string> { ["source"] = source },
            bodyJson: body,
            headers: ifMatch is null ? null : Header("If-Match", $"\"{ifMatch}\""),
            query: query);

    private static SourceMode Declared(EventIngestionEndpointHost host, Source source, EventTypeMode mode) =>
        SourceMode.Declare(FabIdentifier.From("dresden"), source, mode, OperatorIdentifier.From(Subject), host.Clock.Object);

    private static string SourceModeRefusal(string detail) =>
        EventIngestionEndpointHost.Problem(400, "SOURCE_MODE_INVALID_INPUT", detail, BadRequestType);

    private static string EventTypeRefusal(string detail) =>
        EventIngestionEndpointHost.Problem(400, "EVENT_TYPE_INVALID_INPUT", detail, BadRequestType);
}
