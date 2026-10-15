using Moq;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins what <c>PATCH /cameras/{camera}</c> answers, and above all the order in which it refuses
/// (spec 366, #2931, FR-003 and FR-007), so that splitting <c>Patch</c> under ADR-0084's thirty
/// lines cannot reorder it. The order is: fab, then <c>If-Match</c>, then exactly-one-attribute,
/// then the camera identifier, then the operator, then the attribute's own parse.
///
/// <para>
/// The fab comes first for a security reason, not a stylistic one: a 428 or a 400 answered before
/// the fab is checked would tell a caller outside that fab that the camera exists, which is the
/// enumeration spec 028 FR-006 exists to prevent. Every adjacent pair of steps has its own case
/// below, each built so that both steps would refuse and only the earlier one is allowed to win.
/// </para>
///
/// <para>
/// The real rename and change-address handlers run; only the repository is a double. Refusals
/// that are exceptions in the running service (no fab, no subject claim, an empty identifier) are
/// pinned as the throw, because an in-process drive has no global exception handler.
/// </para>
/// </summary>
public class CameraPatchEndpointCharacterisationTests
{
    private const string Patch = "/cameras/{camera:guid}";

    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";

    private const string PreconditionRequired =
        """{"title":"IF_MATCH_REQUIRED","status":428,"detail":"This request must be conditional. Send If-Match with the version the resource was read at."}""";

    private static readonly Guid Subject = new("0190a0f0-0000-7000-8000-000000000001");

    private static readonly string NotBoth =
        InvalidRequest("Send either rtspUrl or name, not both: each is applied under its own version.");

    private static readonly string Neither = InvalidRequest("Send either rtspUrl or name.");

    [Fact]
    public async Task A_caller_in_several_fabs_who_names_none_is_refused_on_the_fab_before_the_precondition()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Send(host, Guid.NewGuid(), Both(), Caller("dresden", "bremen"), ifMatch: null);

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(
            $$"""{"type":"{{BadRequestType}}","title":"CAMERA_FAB_REQUIRED","status":400,"detail":"You are assigned to more than one fab; name the one this applies to with ?fabId=."}""");
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_caller_who_holds_no_fab_is_refused_on_the_fab_before_the_precondition_and_nothing_is_looked_up()
    {
        await using var host = new CameraCatalogEndpointHost();

        await Should.ThrowAsync<FabAuthorizationException>(
            Send(host, Guid.NewGuid(), Both(), Caller(), ifMatch: null));

        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_named_fab_is_checked_with_the_guard_before_the_precondition()
    {
        await using var host = new CameraCatalogEndpointHost();
        host.FabGuard
            .Setup(guard => guard.EnsureAccessAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), "munich", It.IsAny<CancellationToken>()))
            .ThrowsAsync(FabAuthorizationException.ForNoFabMembership());

        await Should.ThrowAsync<FabAuthorizationException>(
            Send(host, Guid.NewGuid(), Both(), Caller("dresden"), ifMatch: null, query: "?fabId=munich"));

        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_missing_precondition_is_refused_with_428_before_the_attributes_are_counted_when_both_are_sent()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Send(host, Guid.NewGuid(), Both(), Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(428);
        response.Body.ShouldBe(PreconditionRequired);
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_missing_precondition_is_refused_with_428_before_the_attributes_are_counted_when_neither_is_sent()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Send(host, Guid.NewGuid(), """{}""", Caller("dresden"), ifMatch: null);

        response.Status.ShouldBe(428);
        response.Body.ShouldBe(PreconditionRequired);
    }

    [Fact]
    public async Task A_request_carrying_both_attributes_is_refused_with_the_not_both_message()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Send(host, Guid.NewGuid(), Both(), Caller("dresden"), ifMatch: "3");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NotBoth);
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_request_carrying_neither_attribute_is_refused_with_the_either_message()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Send(host, Guid.NewGuid(), """{}""", Caller("dresden"), ifMatch: "3");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(Neither);
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Whitespace_in_both_attributes_counts_as_neither()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Send(host, Guid.NewGuid(), """{"rtspUrl":"  ","name":"  "}""", Caller("dresden"), ifMatch: "3");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(Neither);
    }

    [Fact]
    public async Task The_attribute_count_is_refused_before_the_camera_identifier_is_checked()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Send(host, Guid.Empty, Both(), Caller("dresden"), ifMatch: "3");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(NotBoth);
    }

    [Fact]
    public async Task An_empty_camera_identifier_is_refused_before_the_operator_is_resolved()
    {
        await using var host = new CameraCatalogEndpointHost();

        var thrown = await Should.ThrowAsync<ArgumentException>(
            Send(host, Guid.Empty, """{"name":"Dock 2"}""", CameraCatalogEndpointHost.WithoutSubject("dresden"), ifMatch: "3"));

        thrown.ShouldNotBeOfType<InvalidOperationException>();
        thrown.Message.ShouldStartWith("value");
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_operator_is_resolved_before_the_attribute_is_parsed()
    {
        await using var host = new CameraCatalogEndpointHost();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            Send(host, Guid.NewGuid(), TooLongName(), CameraCatalogEndpointHost.WithoutSubject("dresden"), ifMatch: "3"));

        thrown.Message.ShouldBe("Authenticated principal is missing the subject claim. Available claims: groups=/fabs/dresden");
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_principal_with_no_subject_claim_cannot_patch_a_camera()
    {
        await using var host = new CameraCatalogEndpointHost();

        await Should.ThrowAsync<InvalidOperationException>(
            Send(host, Guid.NewGuid(), """{"name":"Dock 2"}""", CameraCatalogEndpointHost.WithoutSubject("dresden"), ifMatch: "3"));

        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_name_is_refused_with_the_name_message()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Send(host, Guid.NewGuid(), TooLongName(), Caller("dresden"), ifMatch: "3");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidRequest("value must be no more than 200 character(s). (Parameter 'value')"));
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_address_is_refused_with_the_address_message()
    {
        await using var host = new CameraCatalogEndpointHost();

        var response = await Send(host, Guid.NewGuid(), """{"rtspUrl":"http://camera"}""", Caller("dresden"), ifMatch: "3");

        response.Status.ShouldBe(400);
        response.Body.ShouldBe(InvalidRequest("value must start with 'rtsp://'. (Parameter 'value')"));
        host.Cameras.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_valid_name_reaches_the_rename_handler_with_the_route_identifier_and_the_callers_fab()
    {
        await using var host = new CameraCatalogEndpointHost();
        var identifier = Guid.NewGuid();

        var response = await Send(host, identifier, """{"name":"Dock 2"}""", Caller("dresden"), ifMatch: "3");

        response.Status.ShouldBe(404);
        response.Body.ShouldBe(NotFound(identifier));
        host.Cameras.Verify(
            cameras => cameras.GetWithinFabAsync(
                FabIdentifier.From("dresden"), CameraIdentifier.From(identifier), It.IsAny<CancellationToken>()),
            Times.Once);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_valid_address_reaches_the_change_address_handler_with_the_route_identifier_and_the_callers_fab()
    {
        await using var host = new CameraCatalogEndpointHost();
        var identifier = Guid.NewGuid();

        var response = await Send(host, identifier, """{"rtspUrl":"rtsp://10.0.0.9/stream"}""", Caller("dresden"), ifMatch: "3");

        response.Status.ShouldBe(404);
        response.Body.ShouldBe(NotFound(identifier));
        host.Cameras.Verify(
            cameras => cameras.GetWithinFabAsync(
                FabIdentifier.From("dresden"), CameraIdentifier.From(identifier), It.IsAny<CancellationToken>()),
            Times.Once);
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_rename_quoting_the_current_version_changes_the_name_and_answers_204()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await Send(host, camera.Id.Value, """{"name":"Dock 2"}""", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(204);
        camera.Name.Value.ShouldBe("Dock 2");
        camera.Url.Value.ShouldBe("rtsp://10.0.0.5/stream");
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_address_change_quoting_the_current_version_changes_the_address_and_answers_204()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await Send(
            host, camera.Id.Value, """{"rtspUrl":"rtsp://10.0.0.9/stream"}""", Caller("dresden"), ifMatch: "0");

        response.Status.ShouldBe(204);
        camera.Url.Value.ShouldBe("rtsp://10.0.0.9/stream");
        camera.Name.Value.ShouldBe("Dock 1");
        host.Cameras.Verify(cameras => cameras.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_rename_quoting_a_stale_version_is_answered_412_with_the_version_the_caller_sent()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await Send(host, camera.Id.Value, """{"name":"Dock 2"}""", Caller("dresden"), ifMatch: "5");

        response.Status.ShouldBe(412);
        response.Body.ShouldBe(Stale(camera.Id.Value));
        camera.Name.Value.ShouldBe("Dock 1");
    }

    [Fact]
    public async Task An_address_change_quoting_a_stale_version_is_answered_412_with_the_version_the_caller_sent()
    {
        await using var host = new CameraCatalogEndpointHost();
        var camera = Existing(host);

        var response = await Send(
            host, camera.Id.Value, """{"rtspUrl":"rtsp://10.0.0.9/stream"}""", Caller("dresden"), ifMatch: "5");

        response.Status.ShouldBe(412);
        response.Body.ShouldBe(Stale(camera.Id.Value));
        camera.Url.Value.ShouldBe("rtsp://10.0.0.5/stream");
    }

    private static string InvalidRequest(string detail) =>
        $$"""{"type":"{{BadRequestType}}","title":"CAMERA_INVALID_REQUEST","status":400,"detail":"{{detail}}"}""";

    private static string NotFound(Guid identifier) =>
        $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"CAMERA_NOT_FOUND","status":404,"detail":"No camera with identifier '{{identifier}}' exists."}""";

    private static string Stale(Guid identifier) =>
        $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.13","title":"CAMERA_VERSION_STALE","status":412,"detail":"Camera '{{identifier}}' is at version 0, not 5. Re-read it before reapplying your change."}""";

    private static string TooLongName() => $$"""{"name":"{{new string('x', 201)}}"}""";

    private static string Both() => """{"rtspUrl":"rtsp://10.0.0.9/stream","name":"Dock 2"}""";

    private static System.Security.Claims.ClaimsPrincipal Caller(params string[] fabs) =>
        CameraCatalogEndpointHost.Operator(Subject, fabs);

    private static Camera Existing(CameraCatalogEndpointHost host)
    {
        var camera = Camera.Register(
            FabIdentifier.From("dresden"),
            CameraName.From("Dock 1"),
            RtspUrl.From("rtsp://10.0.0.5/stream"),
            OperatorIdentifier.From(Subject),
            host.Clock.Object);
        host.Cameras
            .Setup(cameras => cameras.GetWithinFabAsync(
                FabIdentifier.From("dresden"), camera.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Option<Camera>.Some(camera));

        return camera;
    }

    private static Task<InProcessResponse> Send(
        CameraCatalogEndpointHost host,
        Guid identifier,
        string body,
        System.Security.Claims.ClaimsPrincipal user,
        string? ifMatch,
        string? query = null) =>
        host.SendAsync(
            "PATCH",
            Patch,
            user,
            new InProcessRequest(
                Route: new Dictionary<string, string> { ["camera"] = identifier.ToString() },
                BodyJson: body,
                Headers: ifMatch is null ? null : new Dictionary<string, string> { ["If-Match"] = $"\"{ifMatch}\"" },
                Query: query));
}
