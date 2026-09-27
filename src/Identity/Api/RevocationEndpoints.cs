using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.Identity.Application.Queries;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Api;

/// <summary>
/// <c>GET /registered-clients/revoked</c> (spec 270, ADR-0160 §4, FR-007):
/// every service's revocation-snapshot refresher polls this every 5 s.
///
/// <para>
/// No fab resolution, unlike every other list endpoint in this API: the one
/// caller is <c>revocation-list-reader</c>, a platform service account in no
/// fab group, and every consumer needs every fab (plan.md §4.2) — bounding
/// this to a caller's fabs would silently hide revocations the caller's own
/// service still has to enforce.
/// </para>
/// </summary>
public static class RevocationEndpoints
{
    public static IEndpointRouteBuilder MapRevocationEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        RouteGroupBuilder group = app.MapGroup("/registered-clients")
            .RequireAuthorization(Scope.Sse.Identity.Revocations.Read)
            .WithTags("IdentityRevocations");

        group.MapGet("/revoked", ListRevoked)
            .WithName("ListRevokedClients")
            .WithSummary("List every disabled client id and its latest DisabledAt, across all fabs and kinds. Required scope: sse.identity.revocations.read")
            .Produces<RevokedClientsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListRevoked(
        [FromServices] ListRevokedClientsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RevokedClientEntry>, ListClientsError> result =
            await handler.HandleAsync(new ListRevokedClientsQuery(), cancellationToken);

        return result.Match<IResult>(
            onSuccess: entries => Results.Ok(new RevokedClientsResponse(entries)),
            onFailure: error => error.ToProblem());
    }
}
