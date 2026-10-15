using Microsoft.AspNetCore.Builder;
using SmartSentinelEye.SystemVariables.Api;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins the whole SystemVariables route table as it stands (spec 366, #2931, FR-002): every route,
/// verb, name, summary, tag, authorization policy, declared response and the handler's name and
/// parameter list, in registration order. The Map method and the handlers behind it are being
/// split to fit ADR-0084's thirty lines, and the OpenAPI document and the generated clients are
/// derived from exactly this metadata, so none of it may move. The baseline was captured on
/// <c>39b0ef64</c>, before any production edit, and is a literal so a change shows up as a line
/// diff.
/// </summary>
public class SystemVariablesRouteTableCharacterisationTests
{
    private const string Baseline = """
        /system-variables/
          handler List(state: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.SystemVariables.Application.Queries.Handlers.ListVariablesQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute], includeArchived: System.Boolean = 'False' [FromQueryAttribute,OptionalAttribute])
          methods GET
          authorize policy= roles= schemes=
          tags SystemVariables
          authorize policy=sse.variables.read roles= schemes=
          name ListSystemVariables
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List variables in your fabs. Omit fabId to span all of them; name one to narrow (spec 014). Archived variables are excluded unless includeArchived=true, or state=Archived asks for them outright — the same shape GET /cameras uses for retired ones. Every row carries its state. Required scope: sse.variables.read
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.SystemVariables.Application.DTOs.VariableDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /system-variables/-/snapshot
          handler GetSnapshot(overlayIdentifier: System.Guid [FromQueryAttribute], handler: SmartSentinelEye.SystemVariables.Application.Queries.Handlers.GetOverlaySnapshotQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          authorize policy= roles= schemes=
          tags SystemVariables
          authorize policy=sse.variables.read roles= schemes=
          name GetOverlaySnapshot
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Read one overlay's label text with every variable placeholder already resolved to the value current in your fabs — the opening label a kiosk tile renders when it attaches, before any live update arrives. 404 if the overlay is unknown to your fabs. Required scope: sse.variables.read
          produces 200 SmartSentinelEye.SystemVariables.Application.DTOs.ResolvedOverlaySnapshotDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /system-variables/-/resolve
          handler ResolveText(text: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.SystemVariables.Application.Queries.Handlers.ResolveOverlayTextQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          authorize policy= roles= schemes=
          tags SystemVariables
          authorize policy=sse.variables.read roles= schemes=
          name ResolveOverlayText
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Resolve arbitrary text carrying {{variable}} placeholders against your fabs — the overlay editor's live preview of what the wall will render, using the exact same resolution rules GetOverlaySnapshot uses. Unresolvable placeholders (unknown, unset, or archived) keep their literal {{name}} in the resolved text (FR-011); the per-name entries in the response are what distinguish those three cases from each other and from a resolved one. No 404 — resolving text with no matching variables is a 200 with an empty placeholders list, since there is no resource to be absent. Required scope: sse.variables.read
          produces 200 SmartSentinelEye.SystemVariables.Application.DTOs.ResolvedTextPreviewDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /system-variables/{name}
          handler GetOne(name: System.String [], response: Microsoft.AspNetCore.Http.HttpResponse [], handler: SmartSentinelEye.SystemVariables.Application.Queries.Handlers.GetVariableQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          authorize policy= roles= schemes=
          tags SystemVariables
          authorize policy=sse.variables.read roles= schemes=
          name GetSystemVariable
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Read one variable within your fabs. 400 if the name is held in more than one and none is named. Required scope: sse.variables.read
          produces 200 SmartSentinelEye.SystemVariables.Application.DTOs.VariableDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /system-variables/
          handler Define(body: SmartSentinelEye.SystemVariables.Api.Requests.DefineVariableRequest [FromBodyAttribute], services: SmartSentinelEye.SystemVariables.Api.SystemVariableEndpoints+DefineVariableServices [AsParametersAttribute], http: Microsoft.AspNetCore.Http.HttpContext [], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods POST
          authorize policy= roles= schemes=
          tags SystemVariables
          accepts SmartSentinelEye.SystemVariables.Api.Requests.DefineVariableRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.variables.write roles= schemes=
          name DefineSystemVariable
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Define a variable in the resolved fab. Required scope: sse.variables.write
          produces 201 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Http.HttpValidationProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /system-variables/{name}/value
          handler SetValue(name: System.String [], request: Microsoft.AspNetCore.Http.HttpRequest [], body: SmartSentinelEye.SystemVariables.Api.Requests.SetVariableValueRequest [FromBodyAttribute], handler: SmartSentinelEye.SystemVariables.Application.Commands.Handlers.SetVariableValueCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods PUT
          authorize policy= roles= schemes=
          tags SystemVariables
          accepts SmartSentinelEye.SystemVariables.Api.Requests.SetVariableValueRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.variables.write roles= schemes=
          name SetSystemVariableValue
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Set a variable's value in the resolved fab. Required scope: sse.variables.write
          produces 200 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /system-variables/{name}/archive
          handler Archive(name: System.String [], request: Microsoft.AspNetCore.Http.HttpRequest [], handler: SmartSentinelEye.SystemVariables.Application.Commands.Handlers.ArchiveVariableCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods POST
          authorize policy= roles= schemes=
          tags SystemVariables
          authorize policy=sse.variables.write roles= schemes=
          name ArchiveSystemVariable
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Archive a variable in the resolved fab. Required scope: sse.variables.write
          produces 200 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        """;

    [Fact]
    public async Task The_system_variable_route_table_is_unchanged()
    {
        await using var app = WebApplication.CreateBuilder([]).Build();
        app.MapSystemVariableEndpoints();

        var rendered = RouteTableRendering.RenderAll(InProcessEndpointDriver.Endpoints(app));

        rendered.ShouldBe(RouteTableRendering.Normalise(Baseline));
    }
}
