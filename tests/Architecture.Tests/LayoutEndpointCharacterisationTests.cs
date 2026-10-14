using System.Security.Claims;
using Moq;
using SmartSentinelEye.LayoutComposition.Api.Requests;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what the LayoutComposition layout write endpoints answer, and in which order they refuse
/// (spec 363, #2929, FR-003), so that splitting <c>CreateDraft</c>, <c>Publish</c>,
/// <c>Archive</c>, <c>EditDraft</c> and <c>Revert</c> under ADR-0084's thirty lines cannot change
/// a status, a problem body, or which refusal wins when a request is wrong in two ways.
///
/// <para>
/// The real command handlers run; only the repositories behind them are doubles, so an argument
/// transposed inside a moved lambda changes what the repository is asked for, or what the handler
/// reports back, and fails here. Problem bodies are compared as the exact JSON.
/// </para>
///
/// <para>
/// A caller who holds no fab is refused by <see cref="FabAuthorizationException"/> thrown out of
/// the delegate. The global exception handler maps it to 403 in the running service, and an
/// in-process drive does not include that handler, so the throw is what is pinned.
/// </para>
/// </summary>
public class LayoutEndpointCharacterisationTests
{
    private const string Create = "/layouts/";

    private const string Edit = "/layouts/{layoutIdentifier:guid}/revisions/{revisionNumber:int}";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string KeyEmptyBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"IDEMPOTENCY_KEY_MALFORMED","status":400,"detail":"Idempotency-Key was present but empty."}""";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly Guid Camera = new("0190a0f0-0000-7000-8000-0000000000c1");

    private static readonly string[] CallerFabs = ["dresden", "bremen"];

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_with_the_key_message_on_create()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller(), bodyJson: Json(ValidCreate()), headers: Header("Idempotency-Key", " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.Layouts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_before_the_body_is_parsed_on_create()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller(), bodyJson: """{"name":"","grid":null,"tiles":null}""", headers: Header("Idempotency-Key", " "));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
    }

    [Fact]
    public async Task An_invalid_name_is_refused_on_create_with_the_name_message()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller(), bodyJson: Json(ValidCreate() with { Name = " " }));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
        host.Layouts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_missing_grid_is_refused_on_create_with_the_grid_message()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller(), bodyJson: """{"name":"Main wall","tiles":[]}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(GridRefusal);
        host.Layouts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_name_with_a_missing_grid_is_refused_on_the_name_first_on_create()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller(), bodyJson: """{"name":" ","tiles":[]}""");

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
        var body = ValidCreate();

        var response = await host.SendAsync(
            "POST", Create, Caller("dresden"), bodyJson: Json(body), headers: Header("Idempotency-Key", "key-1"));

        response.Status.ShouldBe(201);
        var scope = host.IdempotencyStore.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IIdempotencyStore.BeginAsync))
            .SelectMany(invocation => invocation.Arguments.OfType<IdempotencyScope>())
            .ShouldHaveSingleItem();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /layouts");
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

        var response = await host.SendAsync("POST", Create, Caller("dresden"), bodyJson: Json(ValidCreate()));

        response.Status.ShouldBe(201);
        var identifier = response.Body.Trim('"');
        Guid.TryParse(identifier, out _).ShouldBeTrue();
        response.Location.ShouldBe("/layouts/" + identifier);
        host.Layouts.Verify(
            layouts => layouts.Add(It.Is<Layout>(layout => layout.Id.Value.ToString() == identifier && layout.Fab.Value == "dresden")),
            Times.Once);
        host.Layouts.Verify(layouts => layouts.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_named_fab_is_the_fab_a_layout_is_created_in()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await host.SendAsync(
            "POST", Create, Caller("dresden", "bremen"), bodyJson: Json(ValidCreate()), query: "?fabId=bremen");

        response.Status.ShouldBe(201);
        host.Layouts.Verify(layouts => layouts.Add(It.Is<Layout>(layout => layout.Fab.Value == "bremen")), Times.Once);
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), "bremen", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_caller_who_holds_no_fab_cannot_create_a_layout()
    {
        await using var host = new LayoutCompositionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            host.SendAsync("POST", Create, Caller(), bodyJson: Json(ValidCreate())));

        host.Layouts.Verify(layouts => layouts.Add(It.IsAny<Layout>()), Times.Never);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task An_empty_layout_identifier_is_refused_on_a_revision_command(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Revision(host, command, Guid.Empty, "1", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("layoutIdentifier must be a non-empty Guid."));
        host.Layouts.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task An_empty_layout_identifier_is_refused_before_the_precondition_on_a_revision_command(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Revision(host, command, Guid.Empty, "1", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("layoutIdentifier must be a non-empty Guid."));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task An_empty_layout_identifier_is_refused_before_a_bad_revision_number_on_a_revision_command(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Revision(host, command, Guid.Empty, "0", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("layoutIdentifier must be a non-empty Guid."));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task A_revision_number_below_one_is_refused_on_a_revision_command(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Revision(host, command, Guid.NewGuid(), "0", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(RevisionRefusal);
        host.Layouts.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task A_bad_revision_number_is_refused_before_the_precondition_on_a_revision_command(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Revision(host, command, Guid.NewGuid(), "0", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(RevisionRefusal);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task A_revision_command_without_if_match_is_refused_with_428(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Revision(host, command, Guid.NewGuid(), "1", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(428);
        response.Body.ShouldBe(PreconditionRequired);
        host.Layouts.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task A_missing_if_match_is_refused_before_the_fab_is_resolved_on_a_revision_command(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Revision(host, command, Guid.NewGuid(), "1", Caller(), ifMatch: null);

        response.Status.ShouldBe(428);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task A_caller_who_holds_no_fab_is_refused_before_the_layout_is_looked_up_on_a_revision_command(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            Revision(host, command, Guid.NewGuid(), "1", Caller(), ifMatch: "0"));

        host.Layouts.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task An_unknown_layout_is_answered_404_from_the_repository_asked_for_the_route_identifier_within_the_callers_fabs(
        string command)
    {
        await using var host = new LayoutCompositionEndpointHost();
        var identifier = Guid.NewGuid();

        var response = await Revision(host, command, identifier, "1", Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(404);
        response.Body.ShouldBe(LayoutNotFound(identifier));
        VerifyLookup(host, identifier);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task A_stale_if_match_is_answered_409_naming_the_expected_and_the_actual_version(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();
        var layout = Existing(host, published: command == "revert");

        var response = await Revision(host, command, layout.Id.Value, "1", Caller(CallerFabs), ifMatch: "5");

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(Stale(layout.Id.Value));
        host.Layouts.Verify(layouts => layouts.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task A_revision_the_chain_does_not_have_is_answered_404_naming_the_route_revision(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();
        var layout = Existing(host, published: command == "revert");

        var response = await Revision(host, command, layout.Id.Value, "7", Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(404);
        response.Body.ShouldBe(RevisionNotFound(layout.Id.Value));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("archive")]
    [InlineData("revert")]
    public async Task A_valid_revision_command_answers_200_with_the_revision_number_and_saves(string command)
    {
        await using var host = new LayoutCompositionEndpointHost();
        var layout = Existing(host, published: command == "revert");

        var response = await Revision(host, command, layout.Id.Value, "1", Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(200);
        response.Body.ShouldBe("1");
        host.Layouts.Verify(layouts => layouts.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_empty_layout_identifier_is_refused_on_edit()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Patch(host, Guid.Empty, "1", Json(ValidEdit()), Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("layoutIdentifier must be a non-empty Guid."));
        host.Layouts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_empty_layout_identifier_is_refused_before_the_body_is_parsed_on_edit()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Patch(host, Guid.Empty, "0", """{"tiles":[]}""", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("layoutIdentifier must be a non-empty Guid."));
    }

    [Fact]
    public async Task A_bad_revision_number_is_reported_before_a_missing_grid_on_edit()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Patch(host, Guid.NewGuid(), "0", """{"tiles":[]}""", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(RevisionRefusal);
    }

    [Fact]
    public async Task A_missing_grid_is_refused_before_the_precondition_on_edit()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Patch(host, Guid.NewGuid(), "1", """{"tiles":[]}""", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(GridRefusal);
        host.Layouts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_edit_without_if_match_is_refused_with_428()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var response = await Patch(host, Guid.NewGuid(), "1", Json(ValidEdit()), Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(428);
        response.Body.ShouldBe(PreconditionRequired);
        host.Layouts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_caller_who_holds_no_fab_cannot_edit_a_layout()
    {
        await using var host = new LayoutCompositionEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            Patch(host, Guid.NewGuid(), "1", Json(ValidEdit()), Caller(), ifMatch: "0"));

        host.Layouts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_edit_of_an_unknown_layout_is_answered_404_from_the_repository_asked_for_the_route_identifier_within_the_callers_fabs()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var identifier = Guid.NewGuid();

        var response = await Patch(host, identifier, "1", Json(ValidEdit()), Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(404);
        response.Body.ShouldBe(LayoutNotFound(identifier));
        VerifyLookup(host, identifier);
    }

    [Fact]
    public async Task A_stale_if_match_on_edit_is_answered_409_naming_the_expected_and_the_actual_version()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var layout = Existing(host, published: false);

        var response = await Patch(host, layout.Id.Value, "1", Json(ValidEdit()), Caller(CallerFabs), ifMatch: "5");

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(Stale(layout.Id.Value));
    }

    [Fact]
    public async Task An_edit_of_a_revision_the_chain_does_not_have_is_answered_404_naming_the_route_revision()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var layout = Existing(host, published: false);

        var response = await Patch(host, layout.Id.Value, "7", Json(ValidEdit()), Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(404);
        response.Body.ShouldBe(RevisionNotFound(layout.Id.Value));
    }

    [Fact]
    public async Task A_valid_edit_answers_200_with_the_revision_number_and_replaces_the_grid()
    {
        await using var host = new LayoutCompositionEndpointHost();
        var layout = Existing(host, published: false);
        var edit = new EditDraftRequest(new GridRequest(1, 2), [new TileRequest(Camera, null, 0, 1)]);

        var response = await Patch(host, layout.Id.Value, "1", Json(edit), Caller(CallerFabs), ifMatch: "0");

        response.Status.ShouldBe(200);
        response.Body.ShouldBe("1");
        var revision = layout.Revisions.Single();
        revision.Grid.Rows.ShouldBe(1);
        revision.Grid.Cols.ShouldBe(2);
        revision.Tiles.Single().Position.ShouldBe(GridPosition.From(0, 1));
        host.Layouts.Verify(layouts => layouts.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private const string PreconditionRequired =
        """{"title":"IF_MATCH_REQUIRED","status":428,"detail":"This request must be conditional. Send If-Match with the version the resource was read at."}""";

    private static readonly string NameRefusal = InvalidInput("value must not be null or whitespace. (Parameter 'value')");

    private static readonly string GridRefusal = InvalidInput("Value cannot be null. (Parameter 'grid')");

    private static readonly string RevisionRefusal = InvalidInput("value must be >= 1; got 0. (Parameter 'value')");

    private static string LayoutNotFound(Guid identifier) =>
        $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"LAYOUT_NOT_FOUND","status":404,"detail":"Layout {{identifier}} does not exist."}""";

    private static string RevisionNotFound(Guid identifier) =>
        $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"LAYOUT_REVISION_NOT_FOUND","status":404,"detail":"Layout {{identifier}} has no revision 7."}""";

    private static string Stale(Guid identifier) =>
        $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"LAYOUT_REVISION_STALE","status":409,"detail":"Layout {{identifier}} has changed since version 5 (now 0). Re-read it and reapply the change."}""";

    private static string InvalidInput(string detail) =>
        $$"""{"type":"{{BadRequestType}}","title":"LAYOUT_INVALID_INPUT","status":400,"detail":"{{detail}}"}""";

    private static CreateLayoutRequest ValidCreate() =>
        new("Main wall", new GridRequest(1, 1), [new TileRequest(Camera, null, 0, 0)]);

    private static EditDraftRequest ValidEdit() =>
        new(new GridRequest(1, 1), [new TileRequest(Camera, null, 0, 0)]);

    private static readonly System.Text.Json.JsonSerializerOptions WebOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, WebOptions);

    private static ClaimsPrincipal Caller(params string[] fabs) => LayoutCompositionEndpointHost.Operator(Subject, fabs);

    private static Dictionary<string, string> Header(string name, string value) => new() { [name] = value };

    private static Task<LayoutCompositionEndpointHost.HostResponse> Revision(
        LayoutCompositionEndpointHost host,
        string command,
        Guid identifier,
        string revision,
        ClaimsPrincipal user,
        string? ifMatch) =>
        host.SendAsync(
            "POST",
            $"/layouts/{{layoutIdentifier:guid}}/revisions/{{revisionNumber:int}}/{command}",
            user,
            route: Route(identifier, revision),
            headers: ifMatch is null ? null : Header("If-Match", $"\"{ifMatch}\""));

    private static Task<LayoutCompositionEndpointHost.HostResponse> Patch(
        LayoutCompositionEndpointHost host,
        Guid identifier,
        string revision,
        string body,
        ClaimsPrincipal user,
        string? ifMatch) =>
        host.SendAsync(
            "PATCH",
            Edit,
            user,
            route: Route(identifier, revision),
            bodyJson: body,
            headers: ifMatch is null ? null : Header("If-Match", $"\"{ifMatch}\""));

    private static Dictionary<string, string> Route(Guid identifier, string revision) =>
        new() { ["layoutIdentifier"] = identifier.ToString(), ["revisionNumber"] = revision };

    private static Layout Existing(LayoutCompositionEndpointHost host, bool published)
    {
        var tile = new Tile(
            CameraIdentifier.From(Camera),
            Option<OverlayIdentifier>.None,
            GridPosition.From(0, 0),
            TileSpan.From(1, 1));
        var layout = Layout.CreateDraft(
            FabIdentifier.From("dresden"),
            LayoutName.From("Main wall"),
            GridDimensions.From(1, 1),
            [tile],
            OperatorIdentifier.From(Subject),
            host.Clock.Object);
        if (published)
        {
            layout.Publish(LayoutRevisionNumber.One, OperatorIdentifier.From(Subject), host.Clock.Object);
        }

        host.Layouts
            .Setup(layouts => layouts.GetByIdentifierAsync(
                It.IsAny<IReadOnlyList<FabIdentifier>>(),
                layout.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Layout>.Some(layout));

        return layout;
    }

    private static void VerifyLookup(LayoutCompositionEndpointHost host, Guid identifier) =>
        host.Layouts.Verify(
            layouts => layouts.GetByIdentifierAsync(
                It.Is<IReadOnlyList<FabIdentifier>>(fabs => fabs.Select(fab => fab.Value).SequenceEqual(CallerFabs)),
                LayoutIdentifier.From(identifier),
                It.IsAny<CancellationToken>()),
            Times.Once);
}
