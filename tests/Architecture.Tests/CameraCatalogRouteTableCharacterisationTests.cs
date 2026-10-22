using Microsoft.AspNetCore.Builder;
using SmartSentinelEye.CameraCatalog.Api;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins the whole CameraCatalog route table as it stands (spec 366, #2931, FR-002): every route,
/// verb, name, summary, tag, authorization policy, declared response and the handler's name and
/// parameter list, in registration order. The Map method and the handlers behind it are being
/// split to fit ADR-0084's thirty lines, and the OpenAPI document and the generated clients are
/// derived from exactly this metadata, so none of it may move. The baseline was captured on
/// <c>39b0ef64</c>, before any production edit, and is a literal so a change shows up as a line
/// diff.
/// </summary>
public class CameraCatalogRouteTableCharacterisationTests
{
    private const string Baseline = """
        /cameras/
          handler Register(request: SmartSentinelEye.CameraCatalog.Api.Requests.RegisterCameraRequest [FromBodyAttribute], services: SmartSentinelEye.CameraCatalog.Api.CameraEndpoints+RegisterCameraServices [AsParametersAttribute], httpContext: Microsoft.AspNetCore.Http.HttpContext [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods POST
          tags Cameras
          authorize policy=sse.cameras.write roles= schemes=
          accepts SmartSentinelEye.CameraCatalog.Api.Requests.RegisterCameraRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          name RegisterCamera
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Register a camera in the resolved fab. Omit fabId when you belong to exactly one fab; name it when you belong to several (ADR-0114). Required scope: sse.cameras.write
          produces 201 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Http.HttpValidationProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /cameras/{camera:guid}/retire
          handler Retire(camera: System.Guid [FromRouteAttribute], handler: SmartSentinelEye.CameraCatalog.Application.Commands.Handlers.RetireCameraCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], httpContext: Microsoft.AspNetCore.Http.HttpContext [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods POST
          tags Cameras
          authorize policy=sse.cameras.write roles= schemes=
          name RetireCamera
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Retire a camera. Terminal, and idempotent — retiring one already retired succeeds. Its name becomes available again within its own fab. Required scope: sse.cameras.write
          produces 204 System.Void []
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /cameras/{camera:guid}
          handler Patch(camera: System.Guid [FromRouteAttribute], request: SmartSentinelEye.CameraCatalog.Api.Requests.PatchCameraRequest [FromBodyAttribute], addressHandler: SmartSentinelEye.CameraCatalog.Application.Commands.Handlers.ChangeCameraAddressCommandHandler [FromServicesAttribute], renameHandler: SmartSentinelEye.CameraCatalog.Application.Commands.Handlers.RenameCameraCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], httpContext: Microsoft.AspNetCore.Http.HttpContext [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods PATCH
          tags Cameras
          authorize policy=sse.cameras.write roles= schemes=
          accepts SmartSentinelEye.CameraCatalog.Api.Requests.PatchCameraRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          name PatchCamera
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Correct a camera's RTSP address or its name — exactly one per request, each applied under its own version. Requires If-Match with the version from GET /cameras/{camera} or from a listing row. The fab and identifier are immutable. Required scope: sse.cameras.write
          produces 204 System.Void []
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 412 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /cameras/{camera:guid}/address
          handler GetAddress(camera: System.Guid [FromRouteAttribute], handler: SmartSentinelEye.CameraCatalog.Application.Queries.Handlers.GetCameraAddressQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], httpContext: Microsoft.AspNetCore.Http.HttpContext [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          tags Cameras
          authorize policy=sse.cameras.write roles= schemes=
          name GetCameraAddress
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Read one camera's full stored address, for correcting it. One camera per request. Every read is audited. Required scope: sse.cameras.write
          produces 200 SmartSentinelEye.CameraCatalog.Application.DTOs.CameraAddressDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /cameras/{camera:guid}
          handler Get(camera: System.Guid [FromRouteAttribute], handler: SmartSentinelEye.CameraCatalog.Application.Queries.Handlers.GetCameraQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], response: Microsoft.AspNetCore.Http.HttpResponse [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          tags Cameras
          authorize policy=sse.cameras.read roles= schemes=
          name GetCamera
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Read one camera by its identifier. Returns retired cameras too, with their status. The ETag carries the version to quote when changing it. Required scope: sse.cameras.read
          produces 200 SmartSentinelEye.CameraCatalog.Application.DTOs.CameraDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /cameras/
          handler List(handler: SmartSentinelEye.CameraCatalog.Application.Queries.Handlers.ListCamerasQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute], sort: System.String = '' [FromQueryAttribute,NullableAttribute,OptionalAttribute], order: System.String = '' [FromQueryAttribute,NullableAttribute,OptionalAttribute], offset: System.Nullable`1<System.Int32> = '' [FromQueryAttribute,OptionalAttribute], limit: System.Nullable`1<System.Int32> = '' [FromQueryAttribute,OptionalAttribute], includeRetired: System.Boolean = 'False' [FromQueryAttribute,OptionalAttribute], name: System.String = '' [FromQueryAttribute,NullableAttribute,OptionalAttribute])
          methods GET
          tags Cameras
          authorize policy=sse.cameras.read roles= schemes=
          name ListCameras
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List cameras in your fabs. Omit fabId to span all of them; name one to narrow. A read does not have to choose (spec 015 FR-005). Retired cameras are excluded unless includeRetired=true; every row carries its status. Required scope: sse.cameras.read
          produces 200 SmartSentinelEye.CameraCatalog.Application.DTOs.CameraListPageDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        """;

    [Fact]
    public async Task The_camera_route_table_is_unchanged()
    {
        await using var app = WebApplication.CreateBuilder([]).Build();
        app.MapCameraCatalogEndpoints();

        var rendered = RouteTableRendering.RenderAll(InProcessEndpointDriver.Endpoints(app));

        rendered.ShouldBe(RouteTableRendering.Normalise(Baseline));
    }
}
