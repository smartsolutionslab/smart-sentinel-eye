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
/// Declares, changes and lists the per-<c>(fab, Source)</c> event-type
/// admission policy (spec 269, decision 018). An undeclared pair is
/// discovery; nothing here quarantines an unknown event (that is #2325).
/// </summary>
public static class EventSourcesEndpoints
{
    /// <summary>
    /// Route identity an idempotency key is scoped to (ADR-0142). The resolved
    /// <c>fab/source</c> is appended at the call site, not folded in here: a
    /// multi-fab operator who reuses a key across two fabs (or two sources)
    /// must get two declarations, not the first one replayed onto the second.
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
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", List)
            .RequireAuthorization(Scope.Sse.Events.Read)
            .WithSummary(
                "List the declared source modes of the fabs you hold. An undeclared source is "
                + "discovery and is not listed. Required scope: sse.events.read")
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
                    $"{DeclareEndpoint} {fab.Value}/{source.Value}",
                    declaredBy.Value.ToString())),
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
}
