using Moq;
using SmartSentinelEye.OverlayDesigner.Api.Requests;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what the OverlayDesigner draft write endpoints answer, and in which order they refuse
/// (spec 366, #2931, FR-003), so that splitting <c>CreateDraft</c> and <c>EditDraft</c> under
/// ADR-0084's thirty lines cannot change a status, a problem body, or which refusal wins when a
/// request is wrong in two ways.
///
/// <para>
/// The real command handlers run; only the repository behind them is a double, so an argument
/// transposed inside a moved lambda changes what the repository is asked for, or what the handler
/// reports back, and fails here. Problem bodies are compared as the exact JSON.
/// </para>
/// </summary>
public class OverlayDraftEndpointCharacterisationTests
{
    private const string Create = "/overlays/";

    private const string Edit = "/overlays/{overlayIdentifier:guid}/revisions/{revisionNumber:int}";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string KeyEmptyBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"IDEMPOTENCY_KEY_MALFORMED","status":400,"detail":"Idempotency-Key was present but empty."}""";

    private const string PreconditionRequired =
        """{"title":"IF_MATCH_REQUIRED","status":428,"detail":"This request must be conditional. Send If-Match with the version the resource was read at."}""";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly System.Text.Json.JsonSerializerOptions WebOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static readonly string NameRefusal = InvalidInput("value must not be null or whitespace. (Parameter 'value')");

    private static readonly string ElementsRefusal = InvalidInput("Value cannot be null. (Parameter 'body.Elements')");

    private static readonly string RevisionRefusal = InvalidInput("value must be >= 1; got 0. (Parameter 'value')");

    private static readonly string EmptyIdentifierRefusal = InvalidInput("overlayIdentifier must be a non-empty Guid.");

    private static readonly string UnknownKindRefusal = InvalidInput("kind: unknown ElementKind 'Triangle'. (Parameter 'kind')");

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_with_the_key_message_on_create()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(ValidCreate()), key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.Overlays.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_before_the_elements_are_checked_on_create()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, """{"name":"Main wall"}""", key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
    }

    [Fact]
    public async Task Missing_elements_are_refused_on_create_with_the_elements_message()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, """{"name":"Main wall"}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(ElementsRefusal);
        host.Overlays.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_elements_with_an_invalid_name_are_refused_on_the_elements_first_on_create()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, """{"name":" "}""");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(ElementsRefusal);
    }

    [Fact]
    public async Task An_invalid_name_is_refused_on_create_with_the_name_message()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(ValidCreate() with { Name = " " }));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
        host.Overlays.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_name_with_an_invalid_element_is_refused_on_the_name_first_on_create()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(new CreateOverlayRequest(" ", [Element("Triangle")])));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
    }

    [Fact]
    public async Task An_invalid_element_is_refused_on_create_naming_its_field()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(new CreateOverlayRequest("Main wall", [Element("Triangle")])));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(UnknownKindRefusal);
        host.Overlays.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_empty_element_set_is_refused_on_create_by_the_handler()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(new CreateOverlayRequest("Main wall", [])));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(
            $$"""{"type":"{{BadRequestType}}","title":"OVERLAY_ELEMENTS_EMPTY","status":400,"detail":"A revision must carry at least one element."}""");
        host.Overlays.Verify(overlays => overlays.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_keyed_create_consults_the_store_with_the_route_the_caller_no_fab_and_the_body()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var body = ValidCreate();

        var response = await Post(host, Json(body), key: "key-1");

        response.Status.ShouldBe(201);
        var scope = host.IdempotencyStore.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IIdempotencyStore.BeginAsync))
            .SelectMany(invocation => invocation.Arguments.OfType<IdempotencyScope>())
            .ShouldHaveSingleItem();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /overlays");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.None);
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(body));
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_unkeyed_create_answers_201_with_the_location_and_never_touches_the_store()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(ValidCreate()));

        response.Status.ShouldBe(201);
        var identifier = response.Body.Trim('"');
        Guid.TryParse(identifier, out _).ShouldBeTrue();
        response.Location.ShouldBe("/overlays/" + identifier);
        host.Overlays.Verify(
            overlays => overlays.Add(It.Is<Overlay>(overlay =>
                overlay.Id.Value.ToString() == identifier
                && overlay.Name.Value == "Main wall"
                && overlay.Revisions.Single().Elements.Count == 1
                && overlay.Revisions.Single().Elements[0].Kind == ElementKind.Box
                && overlay.Revisions.Single().Elements[0].Color == OverlayColor.From("#FF0000"))),
            Times.Once);
        host.Overlays.Verify(overlays => overlays.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_name_already_taken_is_answered_409_from_the_handler()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);
        host.Overlays
            .Setup(overlays => overlays.GetByNameAsync(OverlayName.From("Main wall"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Overlay>.Some(existing));

        var response = await Post(host, Json(ValidCreate()));

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"OVERLAY_NAME_TAKEN","status":409,"detail":"A non-archived overlay with the name 'Main wall' already exists."}""");
        host.Overlays.Verify(overlays => overlays.Add(It.IsAny<Overlay>()), Times.Never);
    }

    [Fact]
    public async Task An_empty_overlay_identifier_is_refused_on_edit()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Patch(host, Guid.Empty, "1", Json(ValidEdit()), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EmptyIdentifierRefusal);
        host.Overlays.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_empty_overlay_identifier_with_missing_elements_is_refused_on_the_identifier_first_on_edit()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Patch(host, Guid.Empty, "0", "{}", ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(EmptyIdentifierRefusal);
    }

    [Fact]
    public async Task Missing_elements_are_refused_before_the_revision_number_on_edit()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Patch(host, Guid.NewGuid(), "0", "{}", ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(ElementsRefusal);
    }

    [Fact]
    public async Task A_revision_number_below_one_is_refused_on_edit_with_the_revision_message()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Patch(host, Guid.NewGuid(), "0", Json(ValidEdit()), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(RevisionRefusal);
        host.Overlays.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_revision_number_is_refused_before_an_invalid_element_on_edit()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Patch(
            host, Guid.NewGuid(), "0", Json(new EditDraftRequest([Element("Triangle")])), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(RevisionRefusal);
    }

    [Fact]
    public async Task An_invalid_element_is_refused_on_edit_naming_its_field()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Patch(
            host, Guid.NewGuid(), "1", Json(new EditDraftRequest([Element("Triangle")])), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(UnknownKindRefusal);
        host.Overlays.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_elements_are_refused_before_the_precondition_on_edit()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Patch(host, Guid.NewGuid(), "1", "{}", ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(ElementsRefusal);
        host.Overlays.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_valid_edit_without_if_match_is_refused_with_428()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Patch(host, Guid.NewGuid(), "1", Json(ValidEdit()), ifMatch: null);

        response.Status.ShouldBe(428);
        response.Body.ShouldBe(PreconditionRequired);
        host.Overlays.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_edit_of_an_unknown_overlay_is_answered_404_from_the_repository_asked_for_the_route_identifier()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var identifier = Guid.NewGuid();

        var response = await Patch(host, identifier, "1", Json(ValidEdit()), ifMatch: "0");

        response.Status.ShouldBe(404);
        response.Body.ShouldBe(
            $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"OVERLAY_NOT_FOUND","status":404,"detail":"Overlay {{identifier}} does not exist."}""");
        host.Overlays.Verify(
            overlays => overlays.GetByIdentifierAsync(OverlayIdentifier.From(identifier), It.IsAny<CancellationToken>()),
            Times.Once);
        host.Overlays.Verify(overlays => overlays.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_edit_quoting_a_stale_version_is_answered_409_by_the_handler()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);

        var response = await Patch(host, existing.Id.Value, "1", Json(ValidEdit()), ifMatch: "5");

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(
            $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"OVERLAY_REVISION_STALE","status":409,"detail":"Overlay {{existing.Id.Value}} has changed since version 5 (now 0). Re-read it and reapply the change."}""");
        host.Overlays.Verify(overlays => overlays.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_valid_edit_replaces_the_draft_elements_and_answers_200_with_the_revision_number()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);

        var response = await Patch(
            host,
            existing.Id.Value,
            "1",
            Json(new EditDraftRequest([Element("Ellipse", color: "#00B900")])),
            ifMatch: "0");

        response.Status.ShouldBe(200);
        response.Body.ShouldBe("1");
        var edited = existing.Revisions.Single().Elements.ShouldHaveSingleItem();
        edited.Kind.ShouldBe(ElementKind.Ellipse);
        edited.Color.ShouldBe(OverlayColor.From("#00B900"));
        host.Overlays.Verify(overlays => overlays.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Box", "0.11", "0.23", "0.31", "0.47", "#12345678")]
    [InlineData("Ellipse", "0.41", "0.52", "0.13", "0.24", "#87A2BE")]
    public async Task A_shape_keeps_each_of_its_values_in_its_own_place_on_create(
        string kind, string x, string y, string width, string height, string color)
    {
        await using var host = new OverlayDesignerEndpointHost();
        var added = CaptureAdded(host);

        var response = await Post(
            host, Json(new CreateOverlayRequest("Main wall", [Shape(kind, color, D(x), D(y), D(width), D(height))])));

        response.Status.ShouldBe(201);
        AssertShape(
            added().Revisions.Single().Elements.ShouldHaveSingleItem(),
            kind,
            color,
            D(x),
            D(y),
            D(width),
            D(height));
    }

    [Theory]
    [InlineData("Box", "0.11", "0.23", "0.31", "0.47", "#12345678")]
    [InlineData("Ellipse", "0.41", "0.52", "0.13", "0.24", "#87A2BE")]
    public async Task A_shape_keeps_each_of_its_values_in_its_own_place_on_edit(
        string kind, string x, string y, string width, string height, string color)
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);

        var response = await Patch(
            host,
            existing.Id.Value,
            "1",
            Json(new EditDraftRequest([Shape(kind, color, D(x), D(y), D(width), D(height))])),
            ifMatch: "0");

        response.Status.ShouldBe(200);
        AssertShape(
            existing.Revisions.Single().Elements.ShouldHaveSingleItem(),
            kind,
            color,
            D(x),
            D(y),
            D(width),
            D(height));
    }

    [Fact]
    public async Task A_mixed_box_and_ellipse_set_keeps_each_elements_values_and_order_on_create()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var added = CaptureAdded(host);
        var box = Shape("Box", "#112233", 0.11m, 0.23m, 0.31m, 0.47m);
        var ellipse = Shape("Ellipse", "#44556677", 0.53m, 0.17m, 0.29m, 0.41m);

        var response = await Post(host, Json(new CreateOverlayRequest("Main wall", [box, ellipse])));

        response.Status.ShouldBe(201);
        var elements = added().Revisions.Single().Elements;
        elements.Count.ShouldBe(2);
        AssertShape(elements[0], "Box", "#112233", 0.11m, 0.23m, 0.31m, 0.47m);
        AssertShape(elements[1], "Ellipse", "#44556677", 0.53m, 0.17m, 0.29m, 0.41m);
    }

    [Fact]
    public async Task A_mixed_ellipse_and_box_set_keeps_each_elements_values_and_order_on_edit()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);
        var ellipse = Shape("Ellipse", "#44556677", 0.53m, 0.17m, 0.29m, 0.41m);
        var box = Shape("Box", "#112233", 0.11m, 0.23m, 0.31m, 0.47m);

        var response = await Patch(host, existing.Id.Value, "1", Json(new EditDraftRequest([ellipse, box])), ifMatch: "0");

        response.Status.ShouldBe(200);
        var elements = existing.Revisions.Single().Elements;
        elements.Count.ShouldBe(2);
        AssertShape(elements[0], "Ellipse", "#44556677", 0.53m, 0.17m, 0.29m, 0.41m);
        AssertShape(elements[1], "Box", "#112233", 0.11m, 0.23m, 0.31m, 0.47m);
    }

    [Fact]
    public async Task A_fully_transparent_shape_colour_is_accepted_and_kept_on_create()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var added = CaptureAdded(host);

        var response = await Post(host, Json(new CreateOverlayRequest("Main wall", [Shape("Box", "#00000000")])));

        response.Status.ShouldBe(201);
        added().Revisions.Single().Elements.ShouldHaveSingleItem().Color.ShouldBe(OverlayColor.From("#00000000"));
    }

    [Theory]
    [InlineData("Box")]
    [InlineData("Ellipse")]
    public async Task A_shape_carrying_text_is_refused_naming_text_on_create(string kind)
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(new CreateOverlayRequest("Main wall", [Shape(kind, text: "hello")])));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput($"text must be absent for a {kind} element. (Parameter 'text')"));
        host.Overlays.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Box")]
    [InlineData("Ellipse")]
    public async Task A_shape_carrying_text_is_refused_naming_text_on_edit(string kind)
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);

        var response = await Patch(
            host, existing.Id.Value, "1", Json(new EditDraftRequest([Shape(kind, text: "hello")])), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput($"text must be absent for a {kind} element. (Parameter 'text')"));
        host.Overlays.Verify(overlays => overlays.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Box")]
    [InlineData("Ellipse")]
    public async Task A_shape_carrying_a_font_size_is_refused_naming_font_size_on_create(string kind)
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(new CreateOverlayRequest("Main wall", [Shape(kind, fontSizePx: 14)])));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput($"fontSizePx must be absent for a {kind} element. (Parameter 'fontSizePx')"));
        host.Overlays.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Box")]
    [InlineData("Ellipse")]
    public async Task A_shape_carrying_a_font_size_is_refused_naming_font_size_on_edit(string kind)
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);

        var response = await Patch(
            host, existing.Id.Value, "1", Json(new EditDraftRequest([Shape(kind, fontSizePx: 14)])), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput($"fontSizePx must be absent for a {kind} element. (Parameter 'fontSizePx')"));
        host.Overlays.Verify(overlays => overlays.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_shape_carrying_text_and_a_font_size_is_refused_naming_text_on_create()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(
            host, Json(new CreateOverlayRequest("Main wall", [Shape("Box", text: "hello", fontSizePx: 14)])));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("text must be absent for a Box element. (Parameter 'text')"));
    }

    [Fact]
    public async Task A_shape_carrying_text_and_a_font_size_is_refused_naming_text_on_edit()
    {
        await using var host = new OverlayDesignerEndpointHost();
        var existing = Existing(host);

        var response = await Patch(
            host,
            existing.Id.Value,
            "1",
            Json(new EditDraftRequest([Shape("Ellipse", text: "hello", fontSizePx: 14)])),
            ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("text must be absent for a Ellipse element. (Parameter 'text')"));
    }

    [Fact]
    public async Task An_out_of_range_position_is_refused_before_the_shape_text_check_on_create()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(new CreateOverlayRequest("Main wall", [Shape("Box", x: 1.5m, text: "hello")])));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput($"normalizedX must be in [0, 1]; got {1.5m}. (Parameter 'normalizedX')"));
    }

    [Fact]
    public async Task An_out_of_range_position_is_refused_before_the_shape_text_check_on_edit()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Patch(
            host, Guid.NewGuid(), "1", Json(new EditDraftRequest([Shape("Box", x: 1.5m, text: "hello")])), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput($"normalizedX must be in [0, 1]; got {1.5m}. (Parameter 'normalizedX')"));
    }

    [Fact]
    public async Task An_invalid_colour_is_refused_before_the_shape_text_check_on_create()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Post(host, Json(new CreateOverlayRequest("Main wall", [Shape("Box", "red", text: "hello")])));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("color must be a six- or eight-digit hex colour, e.g. #RRGGBB or #RRGGBBAA. Got 'red'. (Parameter 'color')"));
    }

    [Fact]
    public async Task An_invalid_colour_is_refused_before_the_shape_text_check_on_edit()
    {
        await using var host = new OverlayDesignerEndpointHost();

        var response = await Patch(
            host, Guid.NewGuid(), "1", Json(new EditDraftRequest([Shape("Ellipse", "red", text: "hello")])), ifMatch: "0");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidInput("color must be a six- or eight-digit hex colour, e.g. #RRGGBB or #RRGGBBAA. Got 'red'. (Parameter 'color')"));
    }

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static Func<Overlay> CaptureAdded(OverlayDesignerEndpointHost host)
    {
        Overlay? added = null;
        host.Overlays.Setup(overlays => overlays.Add(It.IsAny<Overlay>())).Callback<Overlay>(overlay => added = overlay);

        return () => added.ShouldNotBeNull();
    }

    private static ElementRequest Shape(
        string kind,
        string color = "#FF0000",
        decimal x = 0.1m,
        decimal y = 0.1m,
        decimal width = 0.2m,
        decimal height = 0.2m,
        string? text = null,
        int? fontSizePx = null) =>
        new(kind, color, x, y, width, height, text, fontSizePx);

    private static void AssertShape(
        OverlayElement element, string kind, string color, decimal x, decimal y, decimal width, decimal height)
    {
        element.Kind.ShouldBe(ElementKind.From(kind));
        element.Position.X.ShouldBe(x);
        element.Position.Y.ShouldBe(y);
        element.Size.Width.ShouldBe(width);
        element.Size.Height.ShouldBe(height);
        element.Color.ShouldBe(OverlayColor.From(color));
        element.Text.ShouldBeNull();
    }

    private static string InvalidInput(string detail) =>
        $$"""{"type":"{{BadRequestType}}","title":"OVERLAY_INVALID_INPUT","status":400,"detail":"{{detail}}"}""";

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, WebOptions);

    private static ElementRequest Element(string kind, string color = "#FF0000") =>
        new(kind, color, 0.1m, 0.1m, 0.2m, 0.2m, null, null);

    private static CreateOverlayRequest ValidCreate() => new("Main wall", [Element("Box")]);

    private static EditDraftRequest ValidEdit() => new([Element("Box")]);

    private static Task<InProcessResponse> Post(OverlayDesignerEndpointHost host, string body, string? key = null) =>
        host.SendAsync(
            "POST",
            Create,
            OverlayDesignerEndpointHost.Operator(Subject),
            new InProcessRequest(
                BodyJson: body,
                Headers: key is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = key }));

    private static Task<InProcessResponse> Patch(
        OverlayDesignerEndpointHost host,
        Guid identifier,
        string revision,
        string body,
        string? ifMatch) =>
        host.SendAsync(
            "PATCH",
            Edit,
            OverlayDesignerEndpointHost.Operator(Subject),
            new InProcessRequest(
                Route: new Dictionary<string, string>
                {
                    ["overlayIdentifier"] = identifier.ToString(),
                    ["revisionNumber"] = revision,
                },
                BodyJson: body,
                Headers: ifMatch is null ? null : new Dictionary<string, string> { ["If-Match"] = $"\"{ifMatch}\"" }));

    private static Overlay Existing(OverlayDesignerEndpointHost host)
    {
        var element = OverlayElement.Box(
            NormalizedPosition.From(0.1m, 0.1m),
            NormalizedSize.From(0.2m, 0.2m),
            OverlayColor.From("#FF0000"));
        var overlay = Overlay.CreateDraft(
            OverlayName.From("Main wall"), [element], OperatorIdentifier.From(Subject), host.Clock.Object);
        host.Overlays
            .Setup(overlays => overlays.GetByIdentifierAsync(overlay.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Overlay>.Some(overlay));

        return overlay;
    }
}
