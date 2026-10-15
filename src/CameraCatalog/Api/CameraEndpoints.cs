using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.CameraCatalog.Api.Requests;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Application.DTOs;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Api;

/// <summary>
/// Minimal-API endpoint group for the Camera Catalog (ADR-0070). All routes
/// require the admin policy per spec 001-register-camera FR-010.
/// </summary>
public static partial class CameraEndpoints
{
    /// <summary>Route identity an idempotency key is scoped to (ADR-0142).</summary>
    private const string RegisterEndpoint = "POST /cameras";

    public static IEndpointRouteBuilder MapCameraCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        var writes = app.MapGroup("/cameras")
            .WithTags("Cameras")
            .RequireAuthorization(Scope.Sse.Cameras.Write);

        // 403 became reachable when fab resolution landed (spec 015 T024): the
        // caller may name a fab they do not hold, or hold none at all. 400 was
        // already declared but now covers a second cause — a multi-fab operator
        // omitting fabId. Declaring them keeps the generated OpenAPI from
        // claiming a status that can happen cannot; spec 013 shipped this wrong
        // on one endpoint and it took a review to catch.
        writes.MapPost("/", Register)
            .WithName("RegisterCamera")
            .WithSummary(
                "Register a camera in the resolved fab. Omit fabId when you belong to exactly one fab; "
                + "name it when you belong to several (ADR-0114). Required scope: sse.cameras.write")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // 404 rather than 403 for another fab's camera (spec 028 FR-004), and
        // it is declared here so the generated OpenAPI says so: a 403 would
        // confirm the camera exists, letting an operator enumerate another
        // plant's cameras one request at a time. 409 covers the lost update,
        // not the domain conflict — two different races. Retiring is
        // idempotent, so an already-retired camera is 204 rather than a
        // conflict; but the handler loads a camera that already exists,
        // mutates it and saves, so a concurrent write to the same row makes
        // EF's affected-row check disagree and the shared handler answers
        // AGGREGATE_VERSION_STALE (ADR-0113 Layer 2, ADR-0119). The
        // idempotence of the first race says nothing about the second.
        writes.MapPost("/{camera:guid}/retire", Retire)
            .WithName("RetireCamera")
            .WithSummary(
                "Retire a camera. Terminal, and idempotent — retiring one already retired succeeds. "
                + "Its name becomes available again within its own fab. Required scope: sse.cameras.write")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // 412 and 428 are declared because both are reachable and neither is a
        // failure of the request's content: 428 when no If-Match is sent, 412
        // when the version quoted is stale (ADR-0113, no retry on conflict).
        // 409 is the retired camera — terminal, so a correction describes
        // nothing (FR-005).
        // The name became editable with spec 033 (ADR-0120), which is safe here
        // and would not be for a rule or a variable: a camera is addressed by
        // its identifier, so its name is an attribute and nothing refers to the
        // old value. 409 now has a second cause — CAMERA_NAME_TAKEN, which is
        // deliberately not a lost update and never becomes possible by
        // re-reading (ADR-0119).
        writes.MapPatch("/{camera:guid}", Patch)
            .WithName("PatchCamera")
            .WithSummary(
                "Correct a camera's RTSP address or its name — exactly one per request, each applied under "
                + "its own version. Requires If-Match with the version from GET /cameras/{camera} or from a "
                + "listing row. The fab and identifier are immutable. Required scope: sse.cameras.write")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        var reads = app.MapGroup("/cameras")
            .WithTags("Cameras")
            .RequireAuthorization(Scope.Sse.Cameras.Read);

        // 404 for another fab's camera, never 403 — the same choice the retire
        // endpoint makes above, and declared here so the generated OpenAPI says
        // so. This is the endpoint spec 015 had to withdraw FR-006 for: without
        // a single-camera read there was nothing to refuse, and the
        // non-enumeration guarantee had nowhere to live.
        reads.MapGet("/{camera:guid}", Get)
            .WithName("GetCamera")
            .WithSummary(
                "Read one camera by its identifier. Returns retired cameras too, with their status. "
                + "The ETag carries the version to quote when changing it. Required scope: sse.cameras.read")
            .Produces<CameraDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reads.MapGet("/", List)
            .WithName("ListCameras")
            .WithSummary(
                "List cameras in your fabs. Omit fabId to span all of them; name one to narrow. "
                + "A read does not have to choose (spec 015 FR-005). Retired cameras are excluded "
                + "unless includeRetired=true; every row carries its status. "
                + "Required scope: sse.cameras.read")
            .Produces<CameraListPageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterCameraRequest request,
        [AsParameters] RegisterCameraServices services,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(request).IsNotNull();
        Ensure.That(httpContext).IsNotNull();

        if (!IdempotencyHeaders.TryRead(httpContext.Request, out var key, out var keyProblem))
        {
            return keyProblem;
        }

        if (!BoundaryParse.TryParse(() => ParseRegistration(request), "CAMERA_INVALID_REQUEST", out var input, out var problem))
        {
            return problem;
        }

        var (name, url) = input;

        var fabResolution =
            await ResolveWriteFabAsync(httpContext.User, fabId, services.FabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;

        RegisterCameraCommand command = new(fab, name, url, ResolveOperator(httpContext));

        return await IdempotentRequest.ExecuteCreateAsync(
            CreateExecution(key, command, request, services),
            identifier => $"/cameras/{identifier}",
            async token => (await services.Handler.HandleAsync(command, token)).Match(
                onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
                onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem())),
            cancellationToken);
    }

    private static (CameraName Name, RtspUrl Url) ParseRegistration(RegisterCameraRequest request)
    {
        var (name, url) = request;

        return (name, url);
    }

    private static IdempotentExecution CreateExecution(
        Option<IdempotencyKey> key,
        RegisterCameraCommand command,
        RegisterCameraRequest request,
        RegisterCameraServices services) =>
        new(
            key.Map(supplied => IdempotencyScope.For(
                supplied,
                RegisterEndpoint,
                command.RegisteredBy.Value.ToString(),
                Option<string>.Some(command.Fab.Value),
                IdempotencyFingerprint.Of(request))),
            services.Idempotency,
            services.Clock);

    /// <summary>
    /// The registration endpoint's collaborators, bundled with
    /// <c>[AsParameters]</c> so the handler keeps a readable signature (ADR-0084).
    /// </summary>
    private sealed record RegisterCameraServices(
        [FromServices] RegisterCameraCommandHandler Handler,
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    private static async Task<IResult> Retire(
        [FromRoute] Guid camera,
        [FromServices] RetireCameraCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        var fabResolution =
            await ResolveWriteFabAsync(httpContext.User, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;

        RetireCameraCommand command = new(fab, CameraIdentifier.From(camera), ResolveOperator(httpContext));

        var result =
            await handler.HandleAsync(command, cancellationToken);

        // 204 rather than 200: nothing useful comes back. The caller asked for
        // the camera to be retired and it is — returning the aggregate would
        // invite a client to treat the response as a read model.
        return result.Match<IResult>(
            onSuccess: _ => Results.NoContent(),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Patch(
        [FromRoute] Guid camera,
        [FromBody] PatchCameraRequest request,
        [FromServices] ChangeCameraAddressCommandHandler addressHandler,
        [FromServices] RenameCameraCommandHandler renameHandler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        Ensure.That(request).IsNotNull();

        // Fab first — before If-Match is read, before the body is parsed, and
        // before the camera is looked up (FR-007). Answering 428 or 400 for
        // another fab's camera would confirm that camera exists, which is the
        // enumeration FR-006 exists to prevent. The cheap checks are the
        // tempting ones to hoist; they must stay below this.
        var fabResolution = await ResolveWriteFabAsync(httpContext.User, fabId, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        var fab = fabResolution.Value;

        if (!ConcurrencyHeaders.TryReadExpectedVersion(httpContext.Request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var changingAddress = !string.IsNullOrWhiteSpace(request.RtspUrl);
        var changingName = !string.IsNullOrWhiteSpace(request.Name);

        // Exactly one. Each attribute has its own command and its own If-Match
        // check, so a request carrying both would need the second to see a
        // version the first had already advanced — see PatchCameraRequest for
        // why that is not built. Neither is a request that expresses nothing.
        if (changingAddress == changingName)
        {
            return Results.Problem(
                title: "CAMERA_INVALID_REQUEST",
                detail: ExactlyOneAttribute(changingAddress),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var identifier = CameraIdentifier.From(camera);
        var actor = ResolveOperator(httpContext);

        return changingName
            ? await PatchAttributeAsync(
                () => CameraName.From(request.Name),
                name => renameHandler.HandleAsync(new RenameCameraCommand(fab, identifier, name, expectedVersion, actor), cancellationToken))
            : await PatchAttributeAsync(
                () => RtspUrl.From(request.RtspUrl),
                url => addressHandler.HandleAsync(new ChangeCameraAddressCommand(fab, identifier, url, expectedVersion, actor), cancellationToken));
    }

    private static string ExactlyOneAttribute(bool changingAddress) =>
        changingAddress
            ? "Send either rtspUrl or name, not both: each is applied under its own version."
            : "Send either rtspUrl or name.";

    private static async Task<IResult> PatchAttributeAsync<TValue, TError>(
        Func<TValue> parse,
        Func<TValue, Task<Result<CameraIdentifier, TError>>> apply)
        where TError : ApiError
    {
        if (!BoundaryParse.TryParse(parse, "CAMERA_INVALID_REQUEST", out var value, out var problem))
        {
            return problem;
        }

        var result = await apply(value);

        // 204 rather than 200: the caller asked for the address to be a
        // particular value and it is. The new version travels on the ETag for
        // a caller making a second change without re-reading.
        return result.Match<IResult>(
            onSuccess: _ => Results.NoContent(),
            onFailure: error => error.ToProblem());
    }

    private static OperatorIdentifier ResolveOperator(HttpContext httpContext)
    {
        // The JWT 'sub' claim ends up under different names depending on whether
        // System.IdentityModel.Tokens.Jwt remapped it (MapInboundClaims=true ->
        // NameIdentifier URI; false -> raw "sub"). Try the common variants and,
        // as a last resort, the first Guid-valued claim on the principal.
        var subject =
            httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("nameid")?.Value;

        if (subject is null)
        {
            var allClaims = string.Join(", ", httpContext.User.Claims.Select(claim => $"{claim.Type}={claim.Value}"));
            throw new InvalidOperationException(
                $"Authenticated principal is missing the subject claim. Available claims: {allClaims}");
        }

        if (!Guid.TryParse(subject, out var subjectId))
        {
            throw new InvalidOperationException($"Subject claim is not a valid Guid: {subject}.");
        }

        return OperatorIdentifier.From(subjectId);
    }

    /// <summary>
    /// CameraCatalog's binding of the shared decision table (ADR-0114) to its
    /// own <see cref="FabIdentifier"/>. The table itself lives in
    /// <see cref="FabResolution"/>; this feature adds no resolution mechanism,
    /// it applies the existing one (spec 015).
    /// </summary>
    private static async Task<Result<FabIdentifier, IResult>> ResolveWriteFabAsync(
        System.Security.Claims.ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken)
    {
        var resolution = await FabResolution.ResolveForWriteAsync(
            user, fabId, fabGuard, "CAMERA_FAB_REQUIRED", cancellationToken);
        if (resolution.IsFailure)
        {
            return Result<FabIdentifier, IResult>.Failure(resolution.Error);
        }

        try
        {
            return Result<FabIdentifier, IResult>.Success(FabIdentifier.From(resolution.Value));
        }
        catch (ArgumentException ex)
        {
            return Result<FabIdentifier, IResult>.Failure(Results.Problem(
                title: "CAMERA_INVALID_REQUEST", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }
}
