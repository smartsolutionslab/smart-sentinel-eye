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
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>
/// The event-type registry (spec 143, decision 018 — the first of its three
/// parts). Declares which event types a fab expects; nothing on the ingest
/// path reads it (FR-013).
/// </summary>
public static class EventTypesEndpoints
{
    /// <summary>Route identity an idempotency key is scoped to (ADR-0142).</summary>
    private const string RegisterEndpoint = "POST /event-types";

    public static IEndpointRouteBuilder MapEventTypesEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        // No scope at the group level: the group carries two different scopes
        // (FR-010 write, FR-011 read), so each mapping declares its own
        // (plan.md §5; EndpointScopeDeclarationTests).
        RouteGroupBuilder group = app.MapGroup("/event-types").WithTags("EventIngestion");

        group.MapPost("/", Register)
            .RequireAuthorization(Scope.Sse.Events.TypesWrite)
            .WithSummary(
                "Declare an event type the resolved fab expects. Omit fabId when you belong to "
                + "exactly one; name it when you belong to several (ADR-0114). Honours Idempotency-Key "
                + "(ADR-0142). A distinct write scope from event ingest, so an event source cannot "
                + "declare which event types are legitimate (FR-010). Required scope: sse.events.types.write")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", List)
            .RequireAuthorization(Scope.Sse.Events.Read)
            .WithSummary(
                "List the registered event types of the fabs you hold. Retired entries are excluded. "
                + "Required scope: sse.events.read")
            .Produces<IReadOnlyList<RegisteredEventTypeDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{kind}", Retire)
            .RequireAuthorization(Scope.Sse.Events.TypesWrite)
            .WithSummary(
                "Retire a registered event type, releasing its name for re-registration. Requires "
                + "If-Match with the version from GET /event-types. Required scope: sse.events.types.write")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterEventTypeRequest body,
        [AsParameters] RegisterEventTypeServices services,
        HttpRequest request,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();

        Kind kind;
        try
        {
            kind = Kind.From(body.Kind);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "EVENT_TYPE_INVALID_INPUT", detail: ex.Message,
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
        OperatorIdentifier registeredBy = user.ToOperatorIdentifier();

        return await IdempotentRequest.ExecuteCreateAsync(
            new IdempotentExecution(
                key.Map(supplied => IdempotencyScope.For(
                    supplied, RegisterEndpoint, registeredBy.Value.ToString())),
                services.Idempotency,
                services.Clock),
            _ => $"/event-types/{kind.Value}",
            token => RegisterAsync(services.Handler, fab, kind, registeredBy, token),
            cancellationToken);
    }

    private static async Task<Result<Guid, IResult>> RegisterAsync(
        RegisterEventTypeCommandHandler handler,
        FabIdentifier fab,
        Kind kind,
        OperatorIdentifier registeredBy,
        CancellationToken cancellationToken)
    {
        Result<RegisteredEventTypeIdentifier, RegisterEventTypeError> result = await handler.HandleAsync(
            new RegisterEventTypeCommand(fab, kind, registeredBy), cancellationToken);

        return result.Match(
            onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
            onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem()));
    }

    /// <summary>
    /// Bundled with <c>[AsParameters]</c> so the handler keeps a readable
    /// signature (ADR-0084), mirroring <c>EventsEndpoints.IngestManualServices</c>.
    /// </summary>
    private sealed record RegisterEventTypeServices(
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] RegisterEventTypeCommandHandler Handler,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    private static async Task<IResult> List(
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        [FromServices] ListEventTypesQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await EventIngestionFabResolution.ResolveReadFabsAsync(
                user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        Result<IReadOnlyList<RegisteredEventTypeDto>, ListEventTypesError> result =
            await handler.HandleAsync(new ListEventTypesQuery(fabsResolution.Value), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Retire(
        string kind,
        HttpRequest request,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        [FromQuery] string? fabId,
        [FromServices] RetireEventTypeCommandHandler handler,
        CancellationToken cancellationToken)
    {
        Kind parsed;
        try
        {
            parsed = Kind.From(kind);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "EVENT_TYPE_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Read before the row is looked up, deliberately (FR-006): looking the
        // row up first would make the 428-vs-404 choice an existence oracle for
        // kinds in fabs the caller cannot read.
        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        Result<IReadOnlyList<FabIdentifier>, IResult> fabsResolution =
            await EventIngestionFabResolution.ResolveReadFabsAsync(
                user, fabId ?? string.Empty, fabGuard, cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        OperatorIdentifier retiredBy = user.ToOperatorIdentifier();

        Result<RegisteredEventTypeIdentifier, RetireEventTypeError> result = await handler.HandleAsync(
            new RetireEventTypeCommand(fabsResolution.Value, parsed, expectedVersion, retiredBy),
            cancellationToken);

        return result.Match<IResult>(
            onSuccess: identifier => Results.Ok(identifier.Value),
            onFailure: error => error.ToProblem());
    }
}
