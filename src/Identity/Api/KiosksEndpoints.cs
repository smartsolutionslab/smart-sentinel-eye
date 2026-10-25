using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.Identity.Api.Requests;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.Queries;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Api;

public static class KiosksEndpoints
{
    /// <summary>Route identity an idempotency key is scoped to (ADR-0142).</summary>
    private const string EnrollEndpoint = "POST /kiosks/enroll";

    public static IEndpointRouteBuilder MapKiosksEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        var group = app.MapGroup("/kiosks")
            .RequireAuthorization(Scope.Sse.Identity.KioskClients.Write)
            .WithTags("IdentityKiosks");

        // 403 is declared on all three because the scope itself produces it:
        // AddScopePolicies builds each sse.* policy as RequireAuthenticatedUser()
        // plus a claim assertion, so a caller who authenticates without the scope
        // is forbidden rather than challenged. All three now reach 403 through
        // IFabAuthorizationGuard as well — DELETE /kiosks/{clientId} runs the fab
        // guard against the required ?fabId= (spec 180 US1), scoped into
        // GetWithinFabAsync's predicate so a fab the caller genuinely holds but
        // that does not own the target answers 404, not 403 (#2240).
        MapEnrollKiosk(group);
        MapDisableKiosk(group);

        var reads = app.MapGroup("/kiosks")
            .RequireAuthorization(Scope.Sse.Identity.KioskClients.Read)
            .WithTags("IdentityKiosks");

        MapListKiosks(reads);

        return app;
    }

    private static void MapEnrollKiosk(RouteGroupBuilder group)
    {
        group.MapPost("/enroll", Enroll)
            .WithName("EnrollKiosk")
            .WithSummary("Enroll a new kiosk in the fab. Required scope: sse.identity.kiosks.write")
            .Produces<KioskCredentialsDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static void MapDisableKiosk(RouteGroupBuilder group)
    {
        // 409 because DisableKioskCommandHandler loads a client that already
        // exists, mutates it and saves — the lost update ADR-0113 Layer 2
        // answers as AGGREGATE_VERSION_STALE.
        group.MapDelete("/{clientId}", Disable)
            .WithName("DisableKiosk")
            .WithSummary("Disable an enrolled kiosk. Required scope: sse.identity.kiosks.write")
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapListKiosks(RouteGroupBuilder reads)
    {
        reads.MapGet("/", List)
            .WithName("ListKiosks")
            .WithSummary("List enrolled kiosks in your fabs. Omit fabId to span all of them; name one to narrow. Required scope: sse.identity.kiosks.read")
            .Produces<IReadOnlyList<RegisteredClientSummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> List(
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] ListKiosksQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        var fabsResolution =
            await IdentityFabResolution.ResolveReadFabsAsync(
                new FabReadRequest(user, fabId),
                fabGuard,
                "KIOSK_INVALID_INPUT",
                cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result =
            await handler.HandleAsync(new ListKiosksQuery(fabs), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> Enroll(
        [FromBody] EnrollKioskRequest body,
        [FromQuery] string fabId,
        [AsParameters] EnrollKioskServices services,
        HttpContext http,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        Ensure.That(http).IsNotNull();

        if (!IdempotencyHeaders.TryRead(http.Request, out var key, out var keyProblem))
        {
            return keyProblem;
        }

        if (!BoundaryParse.TryParse(() => FabIdentifier.From(fabId), "KIOSK_INVALID_INPUT", out var fab, out var invalid))
        {
            return invalid;
        }

        await services.FabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken);

        var actingOperator = user.ToOperatorIdentifier();

        return await IdempotentRequest.ExecuteAsync(
            new IdempotentExecution(
                EnrollScope(key, actingOperator, fab, body),
                services.Idempotency,
                services.Clock),
            async token =>
            {
                var result = await services.Handler.HandleAsync(
                    new EnrollKioskCommand(body.Name, fab, actingOperator), token);

                return result.Match(
                    onSuccess: dto => IdempotentOutcome.Created(
                        dto.RegisteredClientIdentifier, Results.Created($"/kiosks/{dto.ClientId}", dto)),
                    onFailure: error => IdempotentOutcome.NothingCreated(error.ToProblem()));
            },
            (enrolled, token) => ReplayKioskAsync(enrolled, services.Replay, token),
            cancellationToken);
    }

    private static Option<IdempotencyScope> EnrollScope(
        Option<IdempotencyKey> key,
        OperatorIdentifier actingOperator,
        FabIdentifier fab,
        EnrollKioskRequest body) =>
        key.Map(supplied => IdempotencyScope.For(
            supplied,
            EnrollEndpoint,
            actingOperator.Value.ToString(),
            Option<string>.Some(fab.Value),
            IdempotencyFingerprint.Of(body)));

    private static async Task<IResult> ReplayKioskAsync(
        Guid enrolled,
        ReplayRegisteredClientQueryHandler replay,
        CancellationToken cancellationToken)
    {
        var replayed =
            await replay.HandleAsync(
                new ReplayRegisteredClientQuery(RegisteredClientIdentifier.From(enrolled)), cancellationToken);

        // Every field of a kiosk's answer is server-held, so unlike the
        // device endpoint this one needs nothing from the replayed body.
        return replayed.Match<IResult>(
            onSuccess: registered => Results.Created(
                $"/kiosks/{registered.ClientId}",
                registered.ToKioskCredentialsDto()),
            onFailure: error => error.ToProblem());
    }

    /// <summary>
    /// The enrolment endpoint's collaborators, bundled with
    /// <c>[AsParameters]</c> so the handler keeps a readable signature (ADR-0084).
    /// </summary>
    private sealed record EnrollKioskServices(
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] EnrollKioskCommandHandler Handler,
        [FromServices] ReplayRegisteredClientQueryHandler Replay,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    private static async Task<IResult> Disable(
        string clientId,
        [FromQuery] string fabId,
        [FromServices] DisableKioskCommandHandler handler,
        [FromServices] IFabAuthorizationGuard fabGuard,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        // Fab first — before clientId is parsed or looked up. Answering 400 or
        // 404 for another fab's kiosk would confirm that kiosk exists, which is
        // the enumeration GetWithinFabAsync exists to prevent (spec 180 US1).
        FabIdentifier fab;
        try
        {
            fab = FabIdentifier.From(fabId);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "KIOSK_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        await fabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken);

        ClientId parsed;
        try
        {
            parsed = ClientId.From(clientId);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "KIOSK_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await handler.HandleAsync(
            new DisableKioskCommand(parsed, fab), cancellationToken);

        return result.Match<IResult>(
            onSuccess: id => Results.Ok(id.Value),
            onFailure: error => error.ToProblem());
    }
}
