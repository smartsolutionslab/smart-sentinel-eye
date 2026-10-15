using Microsoft.AspNetCore.Builder;
using SmartSentinelEye.StreamDistribution.Api;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins the whole StreamDistribution route table as it stands (spec 366, #2931, FR-002): every
/// route, verb, name, summary, tag, authorization policy, declared response and the handler's name
/// and parameter list, in registration order. The Map method and the handlers behind it are being
/// split to fit ADR-0084's thirty lines, and the OpenAPI document and the generated clients are
/// derived from exactly this metadata, so none of it may move. The baseline was captured on
/// <c>39b0ef64</c>, before any production edit, and is a literal so a change shows up as a line
/// diff. The <c>whep-authorize</c> rate-limit policy and <c>AllowAnonymous</c> on POST
/// /streams/authorize are security controls and are pinned here.
/// </summary>
public class StreamDistributionRouteTableCharacterisationTests
{
    private const string Baseline = """
        /streams/{cameraIdentifier:guid}
          handler GetOne(cameraIdentifier: System.Guid [], handler: SmartSentinelEye.StreamDistribution.Application.Queries.Handlers.GetStreamQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], httpContext: Microsoft.AspNetCore.Http.HttpContext [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          tags Streams
          authorize policy=sse.streams.read roles= schemes=
          name GetStream
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Get one camera's stream, within your fabs. A stream in a fab you do not hold is reported exactly as a camera with no stream (spec 016 FR-006). Required scope: sse.streams.read
          produces 200 SmartSentinelEye.StreamDistribution.Application.DTOs.StreamHealthDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /streams/
          handler ListByCameras(cameraIdentifiers: System.String [FromQueryAttribute], handler: SmartSentinelEye.StreamDistribution.Application.Queries.Handlers.ListStreamsQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], httpContext: Microsoft.AspNetCore.Http.HttpContext [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          tags Streams
          authorize policy=sse.streams.read roles= schemes=
          name ListStreams
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Batch-read stream health within your fabs. Omit fabId to span all of them; name one to narrow. Required scope: sse.streams.read
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.StreamDistribution.Application.DTOs.StreamHealthDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Http.HttpValidationProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /streams/authorize
          handler AuthorizeWhep(body: SmartSentinelEye.StreamDistribution.Api.MediaMtxAuthorizeRequest [FromBodyAttribute], handler: SmartSentinelEye.StreamDistribution.Application.Commands.Handlers.AuthorizeWhepCommandHandler [FromServicesAttribute], apiHandler: SmartSentinelEye.StreamDistribution.Application.Commands.Handlers.AuthorizeMediaMtxApiCommandHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Streams
          accepts SmartSentinelEye.StreamDistribution.Api.MediaMtxAuthorizeRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          allow-anonymous
          rate-limit whep-authorize
          name AuthorizeWhep
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Answer MediaMTX's external-auth hook for a WHEP viewer. No OIDC scope: MediaMTX posts the stream path and the viewer's bearer in the JSON body rather than as an Authorization header, so the route is anonymous and the handler validates that token itself against the fab's Keycloak realm, checking issuer, signature, lifetime and audience, then requires sse.streams.read on it and that the token's fab groups include the stream's own fab (ADR-0161) — a stream not yet fab-attributed is refused to everyone. A missing or invalid token is 401; a valid one without the scope, or outside the stream's fab, is 403. The hook also answers on the action MediaMTX names: read and playback are admitted on that same scope, publish is refused outright because nothing in this product publishes through this hook, and an absent or unrecognised action is refused too — both of those are 403, never 401. A source address over its configured ceiling is refused 429 before any of this runs (spec 208). Since ADR-0171, MediaMTX also reports its own control-API calls through this hook as the api action; those are authorized separately, against a shared secret presented as Basic auth, not against a bearer token or scope.
          produces 200 System.Void []
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 429 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /streams/kiosk-latency
          handler RecordKioskLatency(report: SmartSentinelEye.StreamDistribution.Api.StreamEndpoints+KioskLatencyReport [FromBodyAttribute], httpContext: Microsoft.AspNetCore.Http.HttpContext [])
          methods POST
          tags Streams
          accepts SmartSentinelEye.StreamDistribution.Api.StreamEndpoints+KioskLatencyReport optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.streams.read roles= schemes=
          name RecordKioskLatency
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Record a latency measurement taken in a kiosk browser. The caller sends the elapsed time it already computed, never a start (ADR-0122). Required scope: sse.streams.read
          produces 202 System.Void []
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Http.HttpValidationProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        """;

    [Fact]
    public async Task The_stream_route_table_is_unchanged()
    {
        await using var app = WebApplication.CreateBuilder([]).Build();
        app.MapStreamEndpoints();

        var rendered = RouteTableRendering.RenderAll(InProcessEndpointDriver.Endpoints(app));

        rendered.ShouldBe(RouteTableRendering.Normalise(Baseline));
    }
}
