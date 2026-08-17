using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>
/// Minimal-API endpoint group for EventIngestion (ADR-0070): the manual
/// and webhook write paths plus the read API. Handlers are split across
/// partial files — <c>EventsEndpoints.Writes.cs</c> and
/// <c>EventsEndpoints.Reads.cs</c>.
/// </summary>
public static partial class EventsEndpoints
{
    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder writes = app.MapGroup("/events").WithTags("Events");

        // 403 became reachable with spec 018: the caller may name a fab they do
        // not hold, or hold none at all. 400 was already declared but now
        // covers a second cause — a multi-fab operator omitting fabId. Spec 013
        // shipped this wrong on one endpoint and it took a review to catch.
        writes.MapPost("/manual", IngestManual)
            .RequireAuthorization(Scope.Sse.Events.Write)
            .WithName("IngestManualEvent")
            .WithSummary(
                "Ingest an event into the resolved fab. Omit fabId when you belong to exactly one; "
                + "name it when you belong to several (ADR-0114). The fab is never taken from the "
                + "request unchecked (spec 018 FR-006).")
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        writes.MapPost("/webhook/{integrationName}", IngestWebhook)
            .AllowAnonymous() // auth is the static bearer token, not OIDC
            .WithName("IngestWebhookEvent")
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        RouteGroupBuilder reads = app.MapGroup("/events")
            .RequireAuthorization(Scope.Sse.Events.Read)
            .WithTags("EventsRead");

        reads.MapGet("/", ListEvents)
            .WithName("ListEvents")
            .Produces<EventPageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        reads.MapGet("/{eventId:guid}", GetEvent)
            .WithName("GetEvent")
            .Produces<EventDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        reads.MapGet("/dead-letters", ListDeadLetters)
            .WithName("ListDeadLetters")
            .WithSummary(
                "List rejected deliveries from the fabs you hold. A delivery whose plant could "
                + "not be established from its address is returned to nobody (spec 018 FR-011).")
            .Produces<IReadOnlyList<DeadLetterDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// EventIngestion's binding of the shared decision table (ADR-0114) to its
    /// own <see cref="FabIdentifier"/>, for the one operator-driven write
    /// (spec 018 FR-006).
    ///
    /// <para>
    /// <c>POST /events/webhook/{name}</c> does <b>not</b> use this and must
    /// not: its caller is a machine presenting its own credentials, and it
    /// already checks the fab against them (FR-014).
    /// </para>
    /// </summary>
    private static async Task<(FabIdentifier? Fab, IResult? Problem)> ResolveWriteFabAsync(
        ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken)
    {
        (string resolved, IResult problem) = await FabResolution.ResolveForWriteAsync(
            user, fabId, fabGuard, "EVENT_FAB_REQUIRED", cancellationToken);
        if (problem is not null)
        {
            return (null, problem);
        }

        try
        {
            return (FabIdentifier.From(resolved), null);
        }
        catch (ArgumentException ex)
        {
            return (null, Results.Problem(
                title: "EVENT_INVALID_INPUT", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// The fabs a read may span (spec 018 FR-001, FR-003). Omitting a fab
    /// spans every fab the caller holds; naming one narrows to it; naming one
    /// they do not hold is refused.
    ///
    /// <para>
    /// This replaces taking the fab off the query string and trusting it. The
    /// handlers already filtered on a fab — what was missing was any check
    /// that the caller was entitled to the one they named, which is why the
    /// context looked fab-scoped from every angle except this one.
    /// </para>
    ///
    /// <para>
    /// Parsed per entry rather than all-or-nothing. One group under
    /// <c>/fabs/</c> that is not a usable fab name would otherwise fail the
    /// whole read, hiding every event in the fabs the caller legitimately
    /// holds. Mirrors <c>CameraEndpoints</c>, where that was a real defect.
    /// </para>
    /// </summary>
    private static async Task<(IReadOnlyList<FabIdentifier>? Fabs, IResult? Problem)> ResolveReadFabsAsync(
        ClaimsPrincipal user,
        string fabId,
        IFabAuthorizationGuard fabGuard,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> resolved =
            await FabResolution.ResolveForReadAsync(user, fabId, fabGuard, cancellationToken);

        List<FabIdentifier> fabs = [];
        foreach (string candidate in resolved)
        {
            try
            {
                fabs.Add(FabIdentifier.From(candidate));
            }
            catch (ArgumentException)
            {
                // Skipped, not reported: a caller cannot act on a message about
                // someone else's group configuration, and if nothing is usable
                // the request still fails below.
            }
        }

        if (fabs.Count == 0)
        {
            return (null, Results.Problem(
                title: "EVENT_FAB_REQUIRED",
                detail: "None of your fab groups is a usable fab name.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return (fabs, null);
    }
}
