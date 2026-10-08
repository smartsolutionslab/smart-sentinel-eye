using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.EventIngestion.Api.Requests;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.SourceMode;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>
/// Declares, changes, lists and undeclares the per-<c>(fab, Source)</c>
/// event-type admission policy (spec 269, decision 018). Since spec 317
/// (#2325): an undeclared pair is open (an unregistered kind is stored and
/// fanned out as always); a declared <c>discovery</c> pair holds one in
/// <c>dead_letters</c> for review; a declared <c>strict</c> pair refuses it.
/// </summary>
public static class EventSourcesEndpoints
{
    /// <summary>
    /// Route identity an idempotency key is scoped to (ADR-0142). Spec 302
    /// (#2424/#2492) moves the fab/source distinction from the endpoint
    /// string (which risked overflowing <c>endpoint VARCHAR(128)</c> for a
    /// long source name, and covered neither the fab dimension generally nor
    /// the rest of the body) into the scope's fingerprint and fab, where
    /// every other call site already puts it.
    /// </summary>
    private const string DeclareEndpoint = "POST /event-sources";

    public static IEndpointRouteBuilder MapEventSourcesEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        // No scope at the group level. The group carries two different scopes
        // for writes and reads (FR-013), so each mapping declares its own per
        // plan.md §5, as EndpointScopeDeclarationTests requires.
        RouteGroupBuilder group = app.MapGroup("/event-sources").WithTags("EventIngestion");

        group.MapPost("/", Declare)
            .RequireAuthorization(Scope.Sse.Events.TypesWrite)
            .WithSummary(
                "Declare an admission mode (strict or discovery) for a source in the resolved fab. "
                + "Omit fabId when you belong to exactly one; name it when you belong to several "
                + "(ADR-0114). Honours Idempotency-Key (ADR-0142). Reuses the event-type write scope, "
                + "so an event source cannot decide its own policing (FR-013). "
                + "Required scope: sse.events.types.write")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", List)
            .RequireAuthorization(Scope.Sse.Events.Read)
            .WithSummary(
                "List the declared source modes of the fabs you hold. An undeclared source is open: "
                + "unknown kinds are stored as they always were. A declared discovery source holds "
                + "them; a declared strict source refuses them. An undeclared source is not listed. "
                + "Required scope: sse.events.read")
            .Produces<IReadOnlyList<SourceModeDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{source}/mode", Change)
            .RequireAuthorization(Scope.Sse.Events.TypesWrite)
            .WithSummary(
                "Change a declared source's mode in the resolved fab. Requires If-Match with the "
                + "version from GET /event-sources. Required scope: sse.events.types.write")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapDelete("/{source}", Undeclare)
            .RequireAuthorization(Scope.Sse.Events.TypesWrite)
            .WithSummary(
                "Return a declared source to undeclared in the resolved fab (spec 317, #2325) — the way "
                + "back from strict or discovery. Requires If-Match with the version from "
                + "GET /event-sources. Reuses the event-type write scope, so an event source cannot "
                + "undeclare its own policing. Required scope: sse.events.types.write")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return app;
    }

    private static async Task<IResult> Declare(
        [FromBody] DeclareSourceModeRequest body,
        [AsParameters] DeclareSourceModeServices services,
        HttpRequest request,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

        Source source;
        EventTypeMode mode;
        try
        {
            source = Source.From(body.Source);
            mode = EventTypeMode.From(body.Mode);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "SOURCE_MODE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!IdempotencyHeaders.TryRead(request, out Option<IdempotencyKey> key, out IResult? keyProblem))
        {
            return keyProblem;
        }

        Result<FabIdentifier, IResult> fabResolution =
            await EventIngestionFabResolution.ResolveWriteFabAsync(
                user, fabId ?? string.Empty, services.FabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        FabIdentifier fab = fabResolution.Value;
        OperatorIdentifier declaredBy = user.ToOperatorIdentifier();

        return await IdempotentRequest.ExecuteCreateAsync(
            new IdempotentExecution(
                key.Map(supplied => IdempotencyScope.For(
                    supplied,
                    DeclareEndpoint,
                    declaredBy.Value.ToString(),
                    Option<string>.Some(fab.Value),
                    IdempotencyFingerprint.Of(body))),
                services.Idempotency,
                services.Clock),
            _ => $"/event-sources/{source.Value}",
            token => DeclareAsync(services.Handler, fab, source, mode, declaredBy, token),
            cancellationToken);
    }

    private static async Task<Result<Guid, IResult>> DeclareAsync(
        DeclareSourceModeCommandHandler handler,
        FabIdentifier fab,
        Source source,
        EventTypeMode mode,
        OperatorIdentifier declaredBy,
        CancellationToken cancellationToken)
    {
        Result<SourceModeIdentifier, DeclareSourceModeError> result = await handler.HandleAsync(
            new DeclareSourceModeCommand(fab, source, mode, declaredBy), cancellationToken);

        return result.Match(
            onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
            onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem()));
    }

    /// <summary>
    /// Bundled with <c>[AsParameters]</c> so the handler keeps a readable
    /// signature (ADR-0084), mirroring <c>EventTypesEndpoints.RegisterEventTypeServices</c>.
    /// </summary>
    private sealed record DeclareSourceModeServices(
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] DeclareSourceModeCommandHandler Handler,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    private static async Task<IResult> List(
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        [FromServices] ListSourceModesQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await EventIngestionFabResolution.ResolveReadFabsAsync(
                user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        Result<IReadOnlyList<SourceModeDto>, ListSourceModesError> result =
            await handler.HandleAsync(new ListSourceModesQuery(fabsResolution.Value), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Change(
        string source,
        HttpRequest request,
        [FromBody] ChangeSourceModeRequest body,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        [FromServices] ChangeSourceModeCommandHandler handler,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

        // Read before the row is looked up, deliberately (FR-011): looking the
        // row up first would make the 428-vs-404 choice an existence oracle for
        // sources in fabs the caller cannot read (mirrors EventTypesEndpoints.Retire).
        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        Source parsedSource;
        EventTypeMode parsedMode;
        try
        {
            parsedSource = Source.From(source);
            parsedMode = EventTypeMode.From(body.Mode);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "SOURCE_MODE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<FabIdentifier, IResult> fabResolution =
            await EventIngestionFabResolution.ResolveWriteFabAsync(
                user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        OperatorIdentifier changedBy = user.ToOperatorIdentifier();

        Result<SourceModeIdentifier, ChangeSourceModeError> result = await handler.HandleAsync(
            new ChangeSourceModeCommand(fabResolution.Value, parsedSource, parsedMode, expectedVersion, changedBy),
            cancellationToken);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Ok(identifier.Value),
            onFailure: error => error.ToProblem());
    }

    /// <summary>
    /// Returns a declared pair to undeclared (spec 317, #2325, FR-013, US4) —
    /// mirrors <see cref="Change"/> line for line: <c>If-Match</c> read and
    /// <c>428</c> answered before any lookup (FR-011's reasoning extends
    /// here), then <see cref="Source.From"/>, then the fab, then the handler.
    /// </summary>
    private static async Task<IResult> Undeclare(
        string source,
        HttpRequest request,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        [FromServices] UndeclareSourceModeCommandHandler handler,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        Source parsedSource;
        try
        {
            parsedSource = Source.From(source);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "SOURCE_MODE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        Result<FabIdentifier, IResult> fabResolution =
            await EventIngestionFabResolution.ResolveWriteFabAsync(
                user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fabResolution.IsFailure)
        {
            return fabResolution.Error;
        }

        OperatorIdentifier undeclaredBy = user.ToOperatorIdentifier();

        Result<SourceModeIdentifier, UndeclareSourceModeError> result = await handler.HandleAsync(
            new UndeclareSourceModeCommand(fabResolution.Value, parsedSource, expectedVersion, undeclaredBy),
            cancellationToken);

        return result.Match<IResult>(
            onSuccess: _ => Results.NoContent(),
            onFailure: error => error.ToProblem());
    }
}
