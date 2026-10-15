using Microsoft.AspNetCore.Builder;
using SmartSentinelEye.OverlayDesigner.Api;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins the whole OverlayDesigner route table as it stands (spec 366, #2931, FR-002): every route,
/// verb, name, summary, tag, authorization policy, declared response and the handler's name and
/// parameter list, in registration order. The Map method and the handlers behind it are being
/// split to fit ADR-0084's thirty lines, and the OpenAPI document and the generated clients are
/// derived from exactly this metadata, so none of it may move. The baseline was captured on
/// <c>39b0ef64</c>, before any production edit, and is a literal so a change shows up as a line
/// diff.
/// </summary>
public class OverlayDesignerRouteTableCharacterisationTests
{
    private const string Baseline = """
        /overlays/
          handler CreateDraft(body: SmartSentinelEye.OverlayDesigner.Api.Requests.CreateOverlayRequest [FromBodyAttribute], services: SmartSentinelEye.OverlayDesigner.Api.OverlayEndpoints+CreateOverlayServices [AsParametersAttribute], http: Microsoft.AspNetCore.Http.HttpContext [], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Overlays
          accepts SmartSentinelEye.OverlayDesigner.Api.Requests.CreateOverlayRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.overlays.write roles= schemes=
          name CreateOverlayDraft
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Create a new overlay chain, with its first revision in Draft. Required scope: sse.overlays.write
          produces 201 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Http.HttpValidationProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /overlays/{overlayIdentifier:guid}
          handler GetOne(overlayIdentifier: System.Guid [], response: Microsoft.AspNetCore.Http.HttpResponse [], handler: SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers.GetOverlayQueryHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          tags Overlays
          authorize policy=sse.overlays.read roles= schemes=
          name GetOverlay
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Read one overlay chain by its identifier. Required scope: sse.overlays.read
          produces 200 SmartSentinelEye.OverlayDesigner.Application.DTOs.OverlayDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /overlays/
          handler List(state: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.OverlayDesigner.Application.Queries.Handlers.ListOverlaysQueryHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          tags Overlays
          authorize policy=sse.overlays.read roles= schemes=
          name ListOverlays
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List overlay chains. Required scope: sse.overlays.read
          produces 200 SmartSentinelEye.OverlayDesigner.Api.ListOverlaysResponse [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /overlays/{overlayIdentifier:guid}/revisions/{revisionNumber:int}/publish
          handler Publish(overlayIdentifier: System.Guid [], revisionNumber: System.Int32 [], request: Microsoft.AspNetCore.Http.HttpRequest [], handler: SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers.PublishRevisionCommandHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Overlays
          authorize policy=sse.overlays.write roles= schemes=
          name PublishOverlayRevision
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Publish a Draft revision of an overlay, archiving the previously published one in the same unit of work. Required scope: sse.overlays.write
          produces 200 System.Int32 [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /overlays/{overlayIdentifier:guid}/revisions/{revisionNumber:int}/archive
          handler Archive(overlayIdentifier: System.Guid [], revisionNumber: System.Int32 [], request: Microsoft.AspNetCore.Http.HttpRequest [], handler: SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers.ArchiveRevisionCommandHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Overlays
          authorize policy=sse.overlays.write roles= schemes=
          name ArchiveOverlayRevision
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Archive a revision of an overlay. Required scope: sse.overlays.write
          produces 200 System.Int32 [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /overlays/{overlayIdentifier:guid}/draft
          handler BranchDraft(overlayIdentifier: System.Guid [], request: Microsoft.AspNetCore.Http.HttpRequest [], handler: SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers.BranchDraftRevisionCommandHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Overlays
          authorize policy=sse.overlays.write roles= schemes=
          name BranchDraftOverlayRevision
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Branch a new Draft revision off the overlay chain's current Published revision. Required scope: sse.overlays.write
          produces 201 System.Int32 [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /overlays/{overlayIdentifier:guid}/revisions/{revisionNumber:int}
          handler EditDraft(overlayIdentifier: System.Guid [], revisionNumber: System.Int32 [], request: Microsoft.AspNetCore.Http.HttpRequest [], body: SmartSentinelEye.OverlayDesigner.Api.Requests.EditDraftRequest [FromBodyAttribute], handler: SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers.EditDraftRevisionCommandHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods PATCH
          tags Overlays
          accepts SmartSentinelEye.OverlayDesigner.Api.Requests.EditDraftRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.overlays.write roles= schemes=
          name EditDraftOverlayRevision
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Edit a Draft revision's label in place. Required scope: sse.overlays.write
          produces 200 System.Int32 [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /overlays/{overlayIdentifier:guid}/revisions/{revisionNumber:int}/revert
          handler Revert(overlayIdentifier: System.Guid [], revisionNumber: System.Int32 [], request: Microsoft.AspNetCore.Http.HttpRequest [], handler: SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers.RevertRevisionCommandHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Overlays
          authorize policy=sse.overlays.write roles= schemes=
          name RevertOverlayRevision
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Revert a Published overlay revision to Draft. Required scope: sse.overlays.write
          produces 200 System.Int32 [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        """;

    [Fact]
    public async Task The_overlay_route_table_is_unchanged()
    {
        await using var app = WebApplication.CreateBuilder([]).Build();
        app.MapOverlayEndpoints();

        var rendered = RouteTableRendering.RenderAll(InProcessEndpointDriver.Endpoints(app));

        rendered.ShouldBe(RouteTableRendering.Normalise(Baseline));
    }
}
