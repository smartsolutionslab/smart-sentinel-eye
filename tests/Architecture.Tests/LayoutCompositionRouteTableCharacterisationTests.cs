using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins the whole LayoutComposition route table as it stands (spec 363, #2929, FR-002): every
/// route, verb, name, summary, tag, authorization policy, declared response and the handler's
/// name and parameter list, in registration order. The Map methods and the handlers behind them
/// are being split to fit ADR-0084's thirty lines, and the OpenAPI document and the generated
/// clients are derived from exactly this metadata, so none of it may move.
///
/// <para>
/// The baseline was captured on <c>0296275e</c>, before any production edit, and is a literal so
/// a change shows up as a line diff rather than as two renderings that happen to agree. It
/// contains no absolute path and nothing ordered by hash. Attribute names within one parameter
/// are sorted because reflection does not promise an order.
/// </para>
///
/// <para>
/// <c>CreateDraft</c> and <c>CreateWall</c> declare 400 through <c>ProducesValidationProblem</c>,
/// the other writes through <c>ProducesProblem</c>; both spellings are pinned below.
/// </para>
/// </summary>
public class LayoutCompositionRouteTableCharacterisationTests
{
    private const string Baseline = """
        /layouts/
          handler CreateDraft(body: SmartSentinelEye.LayoutComposition.Api.Requests.CreateLayoutRequest [FromBodyAttribute], services: SmartSentinelEye.LayoutComposition.Api.LayoutEndpoints+CreateLayoutServices [AsParametersAttribute], http: Microsoft.AspNetCore.Http.HttpContext [], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods POST
          tags Layouts
          accepts SmartSentinelEye.LayoutComposition.Api.Requests.CreateLayoutRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.layouts.write roles= schemes=
          name CreateLayoutDraft
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Create a new layout chain in the resolved fab, with its first revision in Draft. Required scope: sse.layouts.write
          produces 201 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Http.HttpValidationProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /layouts/{layoutIdentifier:guid}
          handler GetOne(layoutIdentifier: System.Guid [], response: Microsoft.AspNetCore.Http.HttpResponse [], handler: SmartSentinelEye.LayoutComposition.Application.Queries.Handlers.GetLayoutQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          tags Layouts
          authorize policy=sse.layouts.read roles= schemes=
          name GetLayout
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Read one layout chain by its identifier, within your fabs. Required scope: sse.layouts.read
          produces 200 SmartSentinelEye.LayoutComposition.Application.DTOs.LayoutDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /layouts/
          handler List(state: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.LayoutComposition.Application.Queries.Handlers.ListLayoutsQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          tags Layouts
          authorize policy=sse.layouts.read roles= schemes=
          name ListLayouts
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List layout chains in your fabs. Required scope: sse.layouts.read
          produces 200 SmartSentinelEye.LayoutComposition.Api.ListLayoutsResponse [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /layouts/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/publish
          handler Publish(layoutIdentifier: System.Guid [], revisionNumber: System.Int32 [], request: Microsoft.AspNetCore.Http.HttpRequest [], handler: SmartSentinelEye.LayoutComposition.Application.Commands.Handlers.PublishRevisionCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Layouts
          authorize policy=sse.layouts.write roles= schemes=
          name PublishRevision
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Publish a Draft revision of a layout, archiving the previously published one in the same unit of work. Required scope: sse.layouts.write
          produces 200 System.Int32 [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /layouts/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/archive
          handler Archive(layoutIdentifier: System.Guid [], revisionNumber: System.Int32 [], request: Microsoft.AspNetCore.Http.HttpRequest [], handler: SmartSentinelEye.LayoutComposition.Application.Commands.Handlers.ArchiveRevisionCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Layouts
          authorize policy=sse.layouts.write roles= schemes=
          name ArchiveRevision
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Archive a revision of a layout. Required scope: sse.layouts.write
          produces 200 System.Int32 [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /layouts/{layoutIdentifier:guid}/draft
          handler BranchDraft(layoutIdentifier: System.Guid [], request: Microsoft.AspNetCore.Http.HttpRequest [], handler: SmartSentinelEye.LayoutComposition.Application.Commands.Handlers.BranchDraftRevisionCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Layouts
          authorize policy=sse.layouts.write roles= schemes=
          name BranchDraftRevision
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Branch a new Draft revision off the layout chain's current Published revision. Required scope: sse.layouts.write
          produces 201 System.Int32 [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /layouts/{layoutIdentifier:guid}/revisions/{revisionNumber:int}
          handler EditDraft(layoutIdentifier: System.Guid [], revisionNumber: System.Int32 [], request: Microsoft.AspNetCore.Http.HttpRequest [], body: SmartSentinelEye.LayoutComposition.Api.Requests.EditDraftRequest [FromBodyAttribute], handler: SmartSentinelEye.LayoutComposition.Application.Commands.Handlers.EditDraftRevisionCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods PATCH
          tags Layouts
          accepts SmartSentinelEye.LayoutComposition.Api.Requests.EditDraftRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.layouts.write roles= schemes=
          name EditDraftRevision
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Replace a Draft revision's grid and tile set in place. Required scope: sse.layouts.write
          produces 200 System.Int32 [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /layouts/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/revert
          handler Revert(layoutIdentifier: System.Guid [], revisionNumber: System.Int32 [], request: Microsoft.AspNetCore.Http.HttpRequest [], handler: SmartSentinelEye.LayoutComposition.Application.Commands.Handlers.RevertRevisionCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Layouts
          authorize policy=sse.layouts.write roles= schemes=
          name RevertRevision
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Revert a Published layout revision to Draft. Required scope: sse.layouts.write
          produces 200 System.Int32 [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /walls/
          handler CreateWall(body: SmartSentinelEye.LayoutComposition.Api.Requests.CreateWallRequest [FromBodyAttribute], services: SmartSentinelEye.LayoutComposition.Api.WallEndpoints+CreateWallServices [AsParametersAttribute], http: Microsoft.AspNetCore.Http.HttpContext [], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods POST
          tags Walls
          accepts SmartSentinelEye.LayoutComposition.Api.Requests.CreateWallRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.layouts.write roles= schemes=
          name CreateWall
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Create a new wall in the resolved fab, showing its first scene. Required scope: sse.layouts.write
          produces 201 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Http.HttpValidationProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /walls/{wallIdentifier:guid}
          handler GetOne(wallIdentifier: System.Guid [], response: Microsoft.AspNetCore.Http.HttpResponse [], handler: SmartSentinelEye.LayoutComposition.Application.Queries.Handlers.GetWallQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          tags Walls
          authorize policy=sse.layouts.read roles= schemes=
          name GetWall
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Read one wall by its identifier, within your fabs. Required scope: sse.layouts.read
          produces 200 SmartSentinelEye.LayoutComposition.Application.DTOs.WallDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /walls/
          handler List(handler: SmartSentinelEye.LayoutComposition.Application.Queries.Handlers.ListWallsQueryHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          tags Walls
          authorize policy=sse.layouts.read roles= schemes=
          name ListWalls
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List walls in your fabs. Required scope: sse.layouts.read
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.LayoutComposition.Application.DTOs.WallDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /walls/{wallIdentifier:guid}/scenes
          handler EditScenes(wallIdentifier: System.Guid [], request: Microsoft.AspNetCore.Http.HttpRequest [], body: SmartSentinelEye.LayoutComposition.Api.Requests.EditWallScenesRequest [FromBodyAttribute], handler: SmartSentinelEye.LayoutComposition.Application.Commands.Handlers.EditWallScenesCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods PUT
          tags Walls
          accepts SmartSentinelEye.LayoutComposition.Api.Requests.EditWallScenesRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.layouts.write roles= schemes=
          name EditWallScenes
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Replace a wall's scene set. Moves Showing to the new first scene if it was dropped (US1-15). Required scope: sse.layouts.write
          produces 200 SmartSentinelEye.LayoutComposition.Application.DTOs.WallDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Http.HttpValidationProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /walls/{wallIdentifier:guid}/switch
          handler Switch(wallIdentifier: System.Guid [], request: Microsoft.AspNetCore.Http.HttpRequest [], body: SmartSentinelEye.LayoutComposition.Api.Requests.SwitchWallSceneRequest [FromBodyAttribute], handler: SmartSentinelEye.LayoutComposition.Application.Commands.Handlers.SwitchWallSceneCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Walls
          accepts SmartSentinelEye.LayoutComposition.Api.Requests.SwitchWallSceneRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.layouts.write roles= schemes=
          name SwitchWallScene
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Switch a wall to its next scene or a named one. A switch that leaves Showing unchanged is a no-op. Required scope: sse.layouts.write
          produces 200 SmartSentinelEye.LayoutComposition.Application.DTOs.WallDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Http.HttpValidationProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        """;

    [Fact]
    public async Task The_layout_and_wall_route_table_is_unchanged()
    {
        await using var host = new LayoutCompositionEndpointHost();

        var rendered = Normalise(string.Join("\n", host.Endpoints.Select(Render)));

        rendered.ShouldBe(Normalise(Baseline));
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n").Trim();

    private static string Render(RouteEndpoint endpoint)
    {
        StringBuilder lines = new();
        lines.Append(endpoint.RoutePattern.RawText).Append('\n');

        foreach (var item in endpoint.Metadata.Where(IsContract))
        {
            lines.Append("  ").Append(item switch
            {
                MethodInfo method => "handler " + method.Name + "(" + string.Join(", ", method.GetParameters().Select(RenderParameter)) + ")",
                IHttpMethodMetadata methods => "methods " + string.Join(",", methods.HttpMethods),
                IEndpointNameMetadata name => "name " + name.EndpointName,
                IEndpointSummaryMetadata summary => "summary " + summary.Summary,
                ITagsMetadata tags => "tags " + string.Join(",", tags.Tags),
                IAllowAnonymous => "allow-anonymous",
                IAuthorizeData authorize => $"authorize policy={authorize.Policy} roles={authorize.Roles} schemes={authorize.AuthenticationSchemes}",
                IProducesResponseTypeMetadata produces =>
                    $"produces {produces.StatusCode} {RenderType(produces.Type)} [{string.Join(",", produces.ContentTypes)}]",
                IAcceptsMetadata accepts =>
                    $"accepts {RenderType(accepts.RequestType)} optional={accepts.IsOptional} [{string.Join(",", accepts.ContentTypes)}]",
                _ => "other " + RenderType(item.GetType()),
            }).Append('\n');
        }

        return lines.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Three kinds of metadata are compiler or binder artefacts rather than contract: the async
    /// state-machine marker (it follows whether a handler body awaits), the debugger attribute
    /// Roslyn adds to async methods in Debug builds only, and the per-parameter binding record
    /// (it is derived from the parameter list, which is rendered in full).
    /// </summary>
    private static bool IsContract(object item) =>
        item.GetType().Name != "ParameterBindingMetadata"
        && item is not AsyncStateMachineAttribute
        && item is not DebuggerStepThroughAttribute;

    private static string RenderParameter(ParameterInfo parameter)
    {
        var attributes = parameter.GetCustomAttributes(false)
            .Select(attribute => attribute.GetType().Name)
            .Order(StringComparer.Ordinal);
        var text = $"{parameter.Name}: {RenderType(parameter.ParameterType)}";
        if (parameter.HasDefaultValue)
        {
            text += $" = '{parameter.DefaultValue}'";
        }

        return text + " [" + string.Join(",", attributes) + "]";
    }

    private static string RenderType(Type? type)
    {
        if (type is null)
        {
            return "-";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definition = type.GetGenericTypeDefinition().FullName!;

        return definition + "<" + string.Join(",", type.GetGenericArguments().Select(RenderType)) + ">";
    }
}
