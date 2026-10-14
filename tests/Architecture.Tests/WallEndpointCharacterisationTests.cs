using System.Security.Claims;
using System.Text.Json;
using Moq;
using SmartSentinelEye.LayoutComposition.Api.Requests;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what the LayoutComposition wall write endpoints answer, and in which order they refuse
/// (spec 363, #2929, FR-003), so that splitting <c>CreateWall</c>, <c>EditScenes</c> and
/// <c>Switch</c> under ADR-0084's thirty lines cannot change a status, a problem body, or which
/// refusal wins when a request is wrong in two ways.
///
/// <para>
/// Same method as <see cref="LayoutEndpointCharacterisationTests"/>: the real command handlers
/// run behind the mapped delegate, only the repository and the layout lookup are doubles, and a
/// problem body is compared as the exact JSON. A caller who holds no fab is refused by a
/// <see cref="FabAuthorizationException"/> thrown out of the delegate.
/// </para>
/// </summary>
public class WallEndpointCharacterisationTests
{
    private const string Create = "/walls/";

    private const string Scenes = "/walls/{wallIdentifier:guid}/scenes";

    private const string SwitchPath = "/walls/{wallIdentifier:guid}/switch";

    private const string KeyEmptyBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"IDEMPOTENCY_KEY_MALFORMED","status":400,"detail":"Idempotency-Key was present but empty."}""";

    private const string PreconditionRequired =
        """{"title":"IF_MATCH_REQUIRED","status":428,"detail":"This request must be conditional. Send If-Match with the version the resource was read at."}""";

    private const string EmptyWallIdentifier =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"WALL_INVALID_INPUT","status":400,"detail":"wallIdentifier must be a non-empty Guid."}""";

    private static readonly string NameRefusal = InvalidInput("value must not be null or whitespace. (Parameter 'value')");

    private static readonly string ScenesRefusal = InvalidInput("Value cannot be null. (Parameter 'scenes')");

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly Guid FirstScene = new("0190a0f0-0000-7000-8000-0000000000a1");

    private static readonly Guid SecondScene = new("0190a0f0-0000-7000-8000-0000000000a2");

    private static readonly string[] CallerFabs = ["dresden", "bremen"];

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_with_the_key_message_on_create()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller("dresden"), bodyJson: Json(ValidCreate()), headers: Header("Idempotency-Key", " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_before_the_body_is_parsed_on_create()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller("dresden"), bodyJson: """{"name":" "}""", headers: Header("Idempotency-Key", " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
    }

    [Fact]
    public async Task An_invalid_name_is_refused_on_create_with_the_name_message()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller("dresden"), bodyJson: Json(ValidCreate() with { Name = " " }));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_scenes_are_refused_on_create_with_the_scenes_message()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller("dresden"), bodyJson: """{"name":"Lobby"}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(ScenesRefusal);
        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_name_with_missing_scenes_is_refused_on_the_name_first_on_create()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller("dresden"), bodyJson: """{"name":" "}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
    }

    [Fact]
    public async Task An_invalid_body_from_a_caller_with_no_fab_is_refused_as_input_before_the_fab_is_resolved()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller(), bodyJson: Json(ValidCreate() with { Name = " " }));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
    }

    [Fact]
    public async Task A_keyed_create_consults_the_store_with_the_route_the_caller_the_fab_and_the_body()
    {
        await using var host = new LayoutCompositionEndpointHost();
        ScenesPublishedIn(host, "dresden");
        var body = ValidCreate();

        var response = await host.SendAsync(
            "POST", Create, Caller("dresden"), bodyJson: Json(body), headers: Header("Idempotency-Key", "key-1"));

        response.Status.ShouldBe(201);
        var scope = host.IdempotencyStore.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IIdempotencyStore.BeginAsync))
            .SelectMany(invocation => invocation.Arguments.OfType<IdempotencyScope>())
            .ShouldHaveSingleItem();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /walls");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(body));
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_unkeyed_create_answers_201_with_the_location_and_never_touches_the_store()
    {
        await using var host = new LayoutCompositionEndpointHost();
        ScenesPublishedIn(host, "dresden");

        var response = await host.SendAsync("POST", Create, Caller("dresden"), bodyJson: Json(ValidCreate()));

        response.Status.ShouldBe(201);
        var identifier = response.Body.Trim('"');
        Guid.TryParse(identifier, out _).ShouldBeTrue();
        response.Location.ShouldBe("/walls/" + identifier);
        host.Walls.Verify(
            walls => walls.Add(It.Is<Wall>(wall =>
                wall.Id.Value.ToString() == identifier
                && wall.Fab.Value == "dresden"
                && wall.Name.Value == "Lobby"
                && wall.Scenes.Select(scene => scene.Value).SequenceEqual(new[] { FirstScene, SecondScene }))),
            Times.Once);
        host.Walls.Verify(walls => walls.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_named_fab_is_the_fab_a_wall_is_created_in()
    {
        await using var host = new LayoutCompositionEndpointHost();
        ScenesPublishedIn(host, "bremen");

        var response = await host.SendAsync(
            "POST", Create, Caller("dresden", "bremen"), bodyJson: Json(ValidCreate()), query: "?fabId=bremen");

        response.Status.ShouldBe(201);
        host.Walls.Verify(walls => walls.Add(It.Is<Wall>(wall => wall.Fab.Value == "bremen")), Times.Once);
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), "bremen", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_caller_who_holds_no_fab_cannot_create_a_wall()
    {
        await using var host = new LayoutCompositionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            host.SendAsync("POST", Create, Caller(), bodyJson: Json(ValidCreate())));

        host.Walls.Verify(walls => walls.Add(It.IsAny<Wall>()), Times.Never);
    }

    [Fact]
    public async Task An_empty_wall_identifier_is_refused_on_edit_scenes()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await PutScenes(host, Guid.Empty, Json(ValidScenes()), Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EmptyWallIdentifier);
        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_empty_wall_identifier_is_refused_before_the_body_and_the_precondition_on_edit_scenes()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await PutScenes(host, Guid.Empty, "{}", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EmptyWallIdentifier);
    }

    [Fact]
    public async Task Missing_scenes_are_refused_before_the_precondition_on_edit_scenes()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await PutScenes(host, Guid.NewGuid(), "{}", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(ScenesRefusal);
        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_edit_of_scenes_without_if_match_is_refused_with_428()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await PutScenes(host, Guid.NewGuid(), Json(ValidScenes()), Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(428);
        response.Body.ShouldBe(PreconditionRequired);
        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_missing_if_match_is_refused_before_the_fab_is_resolved_on_edit_scenes()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await PutScenes(host, Guid.NewGuid(), Json(ValidScenes()), Caller(), ifMatch: null);

        response.Status.ShouldBe(428);
    }

    [Fact]
    public async Task A_caller_who_holds_no_fab_is_refused_before_the_wall_is_looked_up_on_edit_scenes()
    {
        await using var host = new LayoutCompositionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            PutScenes(host, Guid.NewGuid(), Json(ValidScenes()), Caller(), ifMatch: "0"));

        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_edit_of_scenes_on_an_unknown_wall_is_answered_404_from_the_repository_asked_for_the_route_identifier_within_the_callers_fabs()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var identifier = Guid.NewGuid();

        var response = await PutScenes(host, identifier, Json(ValidScenes()), Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(404);
        response.Body.ShouldBe(WallNotFound(identifier));
        VerifyLookup(host, identifier);
    }

    [Fact]
    public async Task A_stale_if_match_on_edit_scenes_is_answered_409_naming_the_expected_and_the_actual_version()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var wall = Existing(host);

        var response = await PutScenes(host, wall.Id.Value, Json(ValidScenes()), Caller(CallerFabs), ifMatch: "5");

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(Stale(wall.Id.Value));
        host.Walls.Verify(walls => walls.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_valid_edit_of_scenes_answers_200_with_the_wall_and_saves_the_new_scene_set()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var wall = Existing(host);

        var response = await PutScenes(
            host, wall.Id.Value, Json(new EditWallScenesRequest([SecondScene, FirstScene])), Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(200);
        using var document = JsonDocument.Parse(response.Body);
        document.RootElement.GetProperty("wall").GetGuid().ShouldBe(wall.Id.Value);
        document.RootElement.GetProperty("scenes").EnumerateArray().Select(scene => scene.GetGuid())
            .ShouldBe([SecondScene, FirstScene]);
        wall.Scenes.Select(scene => scene.Value).ShouldBe([SecondScene, FirstScene]);
        host.Walls.Verify(walls => walls.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_empty_wall_identifier_is_refused_on_switch()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Switch(host, Guid.Empty, """{"target":"next"}""", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EmptyWallIdentifier);
        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_empty_wall_identifier_is_refused_before_the_target_and_the_precondition_on_switch()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Switch(host, Guid.Empty, """{"target":"bogus"}""", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EmptyWallIdentifier);
    }

    [Fact]
    public async Task An_unknown_target_is_refused_on_switch_with_the_target_message()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Switch(host, Guid.NewGuid(), """{"target":"bogus"}""", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("Unknown switch target 'bogus'."));
        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_layout_target_without_a_layout_is_refused_on_switch()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Switch(host, Guid.NewGuid(), """{"target":"layout"}""", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("target 'layout' requires a layout identifier."));
        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_unknown_target_is_refused_before_the_precondition_on_switch()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Switch(host, Guid.NewGuid(), """{"target":"bogus"}""", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("Unknown switch target 'bogus'."));
    }

    [Fact]
    public async Task A_switch_without_if_match_is_refused_with_428()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Switch(host, Guid.NewGuid(), """{"target":"next"}""", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(428);
        response.Body.ShouldBe(PreconditionRequired);
        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_missing_if_match_is_refused_before_the_fab_is_resolved_on_switch()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Switch(host, Guid.NewGuid(), """{"target":"next"}""", Caller(), ifMatch: null);

        response.Status.ShouldBe(428);
    }

    [Fact]
    public async Task A_caller_who_holds_no_fab_is_refused_before_the_wall_is_looked_up_on_switch()
    {
        await using var host = new LayoutCompositionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            Switch(host, Guid.NewGuid(), """{"target":"next"}""", Caller(), ifMatch: "0"));

        host.Walls.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_switch_on_an_unknown_wall_is_answered_404_from_the_repository_asked_for_the_route_identifier_within_the_callers_fabs()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var identifier = Guid.NewGuid();

        var response = await Switch(host, identifier, """{"target":"next"}""", Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(404);
        response.Body.ShouldBe(WallNotFound(identifier));
        VerifyLookup(host, identifier);
    }

    [Fact]
    public async Task A_stale_if_match_on_switch_is_answered_409_naming_the_expected_and_the_actual_version()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var wall = Existing(host);

        var response = await Switch(host, wall.Id.Value, """{"target":"next"}""", Caller(CallerFabs), ifMatch: "5");

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(Stale(wall.Id.Value));
        host.Walls.Verify(walls => walls.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_switch_to_next_answers_200_and_moves_the_wall_to_its_next_scene()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var wall = Existing(host);

        var response = await Switch(host, wall.Id.Value, """{"target":"next"}""", Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(200);
        using var document = JsonDocument.Parse(response.Body);
        document.RootElement.GetProperty("showing").GetGuid().ShouldBe(SecondScene);
        wall.Showing.Value.ShouldBe(SecondScene);
        host.Walls.Verify(walls => walls.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_switch_to_a_named_layout_answers_200_and_shows_that_layout()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var wall = Existing(host);

        var response = await Switch(
            host, wall.Id.Value, $$"""{"target":"layout","layout":"{{SecondScene}}"}""", Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(200);
        wall.Showing.Value.ShouldBe(SecondScene);
    }

    private static string InvalidInput(string detail) =>
        $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"WALL_INVALID_INPUT","status":400,"detail":"{{detail}}"}""";

    private static string WallNotFound(Guid identifier) =>
        $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"WALL_NOT_FOUND","status":404,"detail":"Wall {{identifier}} does not exist."}""";

    private static string Stale(Guid identifier) =>
        $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"WALL_STALE","status":409,"detail":"Wall {{identifier}} has changed since version 5 (now 0). Re-read it and reapply the change."}""";

    private static CreateWallRequest ValidCreate() => new("Lobby", [FirstScene, SecondScene]);

    private static EditWallScenesRequest ValidScenes() => new([FirstScene, SecondScene]);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, WebOptions);

    private static ClaimsPrincipal Caller(params string[] fabs) => LayoutCompositionEndpointHost.Operator(Subject, fabs);

    private static Dictionary<string, string> Header(string name, string value) => new() { [name] = value };

    private static Task<LayoutCompositionEndpointHost.HostResponse> PutScenes(
        LayoutCompositionEndpointHost host,
        Guid identifier,
        string body,
        ClaimsPrincipal user,
        string? ifMatch) =>
        host.SendAsync(
            "PUT",
            Scenes,
            user,
            route: Route(identifier),
            bodyJson: body,
            headers: ifMatch is null ? null : Header("If-Match", $"\"{ifMatch}\""));

    private static Task<LayoutCompositionEndpointHost.HostResponse> Switch(
        LayoutCompositionEndpointHost host,
        Guid identifier,
        string body,
        ClaimsPrincipal user,
        string? ifMatch) =>
        host.SendAsync(
            "POST",
            SwitchPath,
            user,
            route: Route(identifier),
            bodyJson: body,
            headers: ifMatch is null ? null : Header("If-Match", $"\"{ifMatch}\""));

    private static Dictionary<string, string> Route(Guid identifier) =>
        new() { ["wallIdentifier"] = identifier.ToString() };

    private static void ScenesPublishedIn(LayoutCompositionEndpointHost host, string fab)
    {
        var first = LayoutIdentifier.From(FirstScene);
        var second = LayoutIdentifier.From(SecondScene);
        host.Lookup
            .Setup(lookup => lookup.FabsOf(It.IsAny<IEnumerable<LayoutIdentifier>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<LayoutIdentifier, FabIdentifier>
            {
                [first] = FabIdentifier.From(fab),
                [second] = FabIdentifier.From(fab),
            });
        host.Lookup
            .Setup(lookup => lookup.PublishedAmong(
                It.IsAny<IEnumerable<LayoutIdentifier>>(),
                It.IsAny<FabIdentifier>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<LayoutIdentifier> { first, second });
    }

    private static Wall Existing(LayoutCompositionEndpointHost host)
    {
        ScenesPublishedIn(host, "dresden");
        var wall = Wall.Create(
            FabIdentifier.From("dresden"),
            WallName.From("Lobby"),
            [LayoutIdentifier.From(FirstScene), LayoutIdentifier.From(SecondScene)],
            OperatorIdentifier.From(Subject),
            host.Clock.Object);
        host.Walls
            .Setup(walls => walls.FindAsync(
                wall.Id,
                It.IsAny<IReadOnlyList<FabIdentifier>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Wall>.Some(wall));

        return wall;
    }

    private static void VerifyLookup(LayoutCompositionEndpointHost host, Guid identifier) =>
        host.Walls.Verify(
            walls => walls.FindAsync(
                WallIdentifier.From(identifier),
                It.Is<IReadOnlyList<FabIdentifier>>(fabs => fabs.Select(fab => fab.Value).SequenceEqual(CallerFabs)),
                It.IsAny<CancellationToken>()),
            Times.Once);
}
