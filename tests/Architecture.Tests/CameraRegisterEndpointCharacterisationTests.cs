using System.Security.Claims;
using Moq;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what <c>POST /cameras</c> answers, and in which order it refuses (spec 366, #2931,
/// FR-003), so that splitting <c>Register</c> under ADR-0084's thirty lines cannot change a
/// status, a problem body, or which refusal wins when a request is wrong in two ways.
///
/// <para>
/// The real register handler runs; only the repository behind it is a double. A caller who holds
/// no fab is refused by <see cref="FabAuthorizationException"/> thrown out of the delegate, and a
/// principal with no subject claim by the <see cref="InvalidOperationException"/> that resolving
/// the operator raises; the global exception handler maps them in the running service, and an
/// in-process drive does not include that handler, so the throw is what is pinned.
/// </para>
/// </summary>
public class CameraRegisterEndpointCharacterisationTests
{
    private const string Register = "/cameras/";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string KeyEmptyBody =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"IDEMPOTENCY_KEY_MALFORMED","status":400,"detail":"Idempotency-Key was present but empty."}""";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly System.Text.Json.JsonSerializerOptions WebOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static readonly string NameRefusal = InvalidRequest("value must not be null or whitespace. (Parameter 'value')");

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_with_the_key_message()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(host, Json(ValidRegistration()), Caller("dresden"), key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_malformed_idempotency_key_is_refused_before_the_body_is_parsed()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(host, Json(ValidRegistration() with { Name = " " }), Caller("dresden"), key: " ");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(KeyEmptyBody);
    }

    [Fact]
    public async Task An_invalid_name_is_refused_with_the_name_message()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(host, Json(ValidRegistration() with { Name = " " }), Caller("dresden"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_address_is_refused_with_the_address_message()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(host, Json(ValidRegistration() with { RtspUrl = "http://camera" }), Caller("dresden"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidRequest("value must start with 'rtsp://'. (Parameter 'value')"));
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_name_with_an_invalid_address_is_refused_on_the_name_first()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(
            host, Json(new RegisterCameraRequest { Name = " ", RtspUrl = "http://camera" }), Caller("dresden"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
    }

    [Fact]
    public async Task An_invalid_body_from_a_caller_with_no_fab_is_refused_as_input_before_the_fab_is_resolved()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(host, Json(ValidRegistration() with { Name = " " }), Caller());

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NameRefusal);
    }

    [Fact]
    public async Task A_caller_in_several_fabs_who_names_none_is_refused_with_the_fab_required_problem()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(host, Json(ValidRegistration()), Caller("dresden", "bremen"));

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(
            $$"""{"type":"{{BadRequestType}}","title":"CAMERA_FAB_REQUIRED","status":400,"detail":"You are assigned to more than one fab; name the one this applies to with ?fabId=."}""");
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_caller_who_holds_no_fab_cannot_register_a_camera()
    {
        await using var host = new CameraCatalogEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(Post(host, Json(ValidRegistration()), Caller()));

        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
    }

    [Fact]
    public async Task A_named_fab_is_the_fab_a_camera_is_registered_in()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(host, Json(ValidRegistration()), Caller("dresden", "bremen"), query: "?fabId=bremen");

        response.Status.ShouldBe(201);
        host.Cameras.Verify(cameras => cameras.Add(It.Is<Camera>(camera => camera.Fab.Value == "bremen")), Times.Once);
        host.FabGuard.Verify(
            guard => guard.EnsureAccessAsync(It.IsAny<ClaimsPrincipal>(), "bremen", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_principal_with_no_subject_claim_cannot_register_a_camera()
    {
        await using var host = new CameraCatalogEndpointHost();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            Post(host, Json(ValidRegistration()), CameraCatalogEndpointHost.WithoutSubject("dresden")));

        thrown.Message.ShouldBe("Authenticated principal is missing the subject claim. Available claims: groups=/fabs/dresden");
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
    }

    [Fact]
    public async Task A_keyed_register_consults_the_store_with_the_route_the_caller_the_fab_and_the_body()
    {
        await using var host = new CameraCatalogEndpointHost();
        var body = ValidRegistration();

        var response = await Post(host, Json(body), Caller("dresden"), key: "key-1");

        response.Status.ShouldBe(201);
        var scope = host.IdempotencyStore.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IIdempotencyStore.BeginAsync))
            .SelectMany(invocation => invocation.Arguments.OfType<IdempotencyScope>())
            .ShouldHaveSingleItem();
        scope.Key.Value.ShouldBe("key-1");
        scope.Endpoint.ShouldBe("POST /cameras");
        scope.Caller.ShouldBe(Subject.ToString());
        scope.Fab.ShouldBe(Option<string>.Some("dresden"));
        scope.Fingerprint.ShouldBe(IdempotencyFingerprint.Of(body));
        host.IdempotencyStore.Verify(
            store => store.CompleteAsync(It.IsAny<IdempotencyClaim>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_unkeyed_register_answers_201_with_the_location_and_never_touches_the_store()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Post(host, Json(ValidRegistration()), Caller("dresden"));

        response.Status.ShouldBe(201);
        var identifier = response.Body.Trim('"');
        Guid.TryParse(identifier, out _).ShouldBeTrue();
        response.Location.ShouldBe("/cameras/" + identifier);
        host.Cameras.Verify(
            cameras => cameras.Add(It.Is<Camera>(camera =>
                camera.Id.Value.ToString() == identifier
                && camera.Fab.Value == "dresden"
                && camera.Name.Value == "Dock 1"
                && camera.Url.Value == "rtsp://10.0.0.5/stream")),
            Times.Once);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        host.IdempotencyStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_name_already_taken_in_the_fab_is_answered_409_from_the_handler()
    {
        await using var host = new CameraCatalogEndpointHost();
        host.Cameras
            .Setup(cameras => cameras.ExistsByNameAsync(
                FabIdentifier.From("dresden"),
                CameraName.From("Dock 1"),
                Option<CameraIdentifier>.None,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await Post(host, Json(ValidRegistration()), Caller("dresden"));

        response.Status.ShouldBe(409);
        response.Body.ShouldBe(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"CAMERA_NAME_TAKEN","status":409,"detail":"A camera named 'Dock 1' already exists in fab 'dresden'."}""");
        host.Cameras.Verify(cameras => cameras.Add(It.IsAny<Camera>()), Times.Never);
    }

    private static string InvalidRequest(string detail) =>
        $$"""{"type":"{{BadRequestType}}","title":"CAMERA_INVALID_REQUEST","status":400,"detail":"{{detail}}"}""";

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, WebOptions);

    private static RegisterCameraRequest ValidRegistration() =>
        new() { Name = "Dock 1", RtspUrl = "rtsp://10.0.0.5/stream" };

    private static ClaimsPrincipal Caller(params string[] fabs) => CameraCatalogEndpointHost.Operator(Subject, fabs);

    private static Task<InProcessResponse> Post(
        CameraCatalogEndpointHost host,
        string body,
        ClaimsPrincipal user,
        string? key = null,
        string? query = null) =>
        host.SendAsync(
            "POST",
            Register,
            user,
            new InProcessRequest(
                BodyJson: body,
                Headers: key is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = key },
                Query: query));
}
