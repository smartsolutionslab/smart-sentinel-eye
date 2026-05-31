using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.AuditObservability.Api;

/// <summary>
/// HTTP read API for AuditObservability (spec 009 FR-008 / 009 /
/// 010). Every endpoint is gated by <c>sse.audit.read</c>; the
/// per-fab + per-resource endpoints additionally run the shared
/// fab guard from <c>ServiceDefaults</c> (spec 008 FR-019).
/// </summary>
public static class AuditEndpoints
{
    public const string GroupClaimType = DefaultFabAuthorizationGuard.GroupClaimType;
    public const string FabGroupPrefix = DefaultFabAuthorizationGuard.FabGroupPrefix;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/audit")
            .RequireAuthorization(Scope.Sse.Audit.Read)
            .WithTags("Audit");

        group.MapGet("/", Search)
            .WithName("SearchAudit")
            .WithSummary("Cross-cutting audit search. Required scope: sse.audit.read")
            .Produces<AuditPageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{resourceKind}/{resourceIdentifier}", GetTimeline)
            .WithName("GetResourceAuditTimeline")
            .WithSummary("Per-resource audit timeline. Required scope: sse.audit.read")
            .Produces<AuditPageDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{auditIdentifier:guid}", GetSingle)
            .WithName("GetAuditEvent")
            .WithSummary("Single audit row + full payload. Required scope: sse.audit.read")
            .Produces<AuditRowDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> Search(
        [FromQuery] string? fabId,
        [FromQuery] Guid? actor,
        [FromQuery] string? actorUsername,
        [FromQuery] string? eventKind,
        [FromQuery] string? resourceKind,
        [FromQuery] string? resourceIdentifier,
        [FromQuery] DateTimeOffset? since,
        [FromQuery] DateTimeOffset? until,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] SearchAuditQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (fabId is not null)
        {
            await fabGuard.EnsureAccessAsync(user, fabId, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<string> callerFabs = ExtractFabSet(user);

        Result<AuditPageDto, SearchAuditError> result = await handler.HandleAsync(
            new SearchAuditQuery(
                Fab: fabId,
                CallerFabs: callerFabs,
                Actor: actor,
                ActorUsername: actorUsername,
                EventKind: eventKind,
                ResourceKind: resourceKind,
                ResourceIdentifier: resourceIdentifier,
                Since: since,
                Until: until,
                PageSize: pageSize ?? 0,
                Cursor: cursor),
            cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> GetTimeline(
        string resourceKind,
        string resourceIdentifier,
        [FromQuery] string fabId,
        [FromQuery] DateTimeOffset? since,
        [FromQuery] DateTimeOffset? until,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] GetResourceTimelineQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await fabGuard.EnsureAccessAsync(user, fabId, cancellationToken).ConfigureAwait(false);

        Result<AuditPageDto, GetResourceTimelineError> result = await handler.HandleAsync(
            new GetResourceTimelineQuery(
                ResourceKind: resourceKind,
                ResourceIdentifier: resourceIdentifier,
                Fab: fabId,
                Since: since,
                Until: until,
                PageSize: pageSize ?? 0,
                Cursor: cursor),
            cancellationToken).ConfigureAwait(false);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> GetSingle(
        Guid auditIdentifier,
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] GetAuditEventQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Result<AuditRowDto, GetAuditEventError> result = await handler.HandleAsync(
            new GetAuditEventQuery(auditIdentifier),
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Results.Problem(
                title: result.Error.Code, detail: result.Error.Message,
                statusCode: (int)result.Error.Status);
        }

        AuditRowDto row = result.Value;
        if (row.Fab is not null)
        {
            await fabGuard.EnsureAccessAsync(user, row.Fab, cancellationToken).ConfigureAwait(false);
        }
        return Results.Ok(row);
    }

    private static List<string> ExtractFabSet(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return [.. user.FindAll(GroupClaimType)
            .SelectMany(claim => claim.Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Where(token => token.StartsWith(FabGroupPrefix, StringComparison.Ordinal))
            .Select(token => token[FabGroupPrefix.Length..])];
    }
}
