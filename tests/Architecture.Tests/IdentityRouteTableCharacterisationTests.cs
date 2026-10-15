using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins the Identity route table this spec touches as it stands (spec 367, #2932, FR-002): every
/// route, verb, name, summary, tag, authorization policy, <c>AllowAnonymous</c>, declared
/// response and the handler's name and parameter list, in registration order. The Map methods
/// and the handlers behind them are being split to fit ADR-0084's thirty lines, and the OpenAPI
/// document and the generated clients are derived from exactly this metadata, so none of it may
/// move.
///
/// <para>
/// The baseline was captured on <c>491d5027</c>, before any production edit, and is a literal so
/// a change shows up as a line diff rather than as two renderings that happen to agree. It
/// contains no absolute path and nothing ordered by hash. Attribute names within one parameter
/// are sorted because reflection does not promise an order. Metadata that follows the build
/// configuration rather than the contract (the async state-machine and debugger-step markers,
/// the binder's per-parameter record) is not rendered, so the literal holds in Debug and Release.
/// </para>
/// </summary>
public class IdentityRouteTableCharacterisationTests
{
    private const string Baseline = """
        /devices/register
          handler Register(body: SmartSentinelEye.Identity.Api.Requests.RegisterDeviceRequest [FromBodyAttribute], fabId: System.String [FromQueryAttribute], services: SmartSentinelEye.Identity.Api.DevicesEndpoints+RegisterDeviceServices [AsParametersAttribute], http: Microsoft.AspNetCore.Http.HttpContext [], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          authorize policy=sse.identity.devices.write roles= schemes=
          tags IdentityDevices
          accepts SmartSentinelEye.Identity.Api.Requests.RegisterDeviceRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          name RegisterDevice
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Register a new PLC or inference device. Required scope: sse.identity.devices.write
          produces 201 SmartSentinelEye.Identity.Application.DTOs.DeviceCredentialsDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /devices/{clientId}
          handler Disable(clientId: System.String [], fabId: System.String [FromQueryAttribute], handler: SmartSentinelEye.Identity.Application.Commands.Handlers.DisableDeviceCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods DELETE
          authorize policy=sse.identity.devices.write roles= schemes=
          tags IdentityDevices
          name DisableDevice
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Disable a registered device. Required scope: sse.identity.devices.write
          produces 200 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /devices/
          handler List(fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], handler: SmartSentinelEye.Identity.Application.Queries.Handlers.ListDevicesQueryHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          authorize policy=sse.identity.devices.read roles= schemes=
          tags IdentityDevices
          name ListDevices
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List registered devices in your fabs. Omit fabId to span all of them; name one to narrow. Required scope: sse.identity.devices.read
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.Identity.Application.DTOs.RegisteredClientSummaryDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /kiosks/enroll
          handler Enroll(body: SmartSentinelEye.Identity.Api.Requests.EnrollKioskRequest [FromBodyAttribute], fabId: System.String [FromQueryAttribute], services: SmartSentinelEye.Identity.Api.KiosksEndpoints+EnrollKioskServices [AsParametersAttribute], http: Microsoft.AspNetCore.Http.HttpContext [], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          authorize policy=sse.identity.kiosks.write roles= schemes=
          tags IdentityKiosks
          accepts SmartSentinelEye.Identity.Api.Requests.EnrollKioskRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          name EnrollKiosk
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Enroll a new kiosk in the fab. Required scope: sse.identity.kiosks.write
          produces 201 SmartSentinelEye.Identity.Application.DTOs.KioskCredentialsDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /kiosks/{clientId}
          handler Disable(clientId: System.String [], fabId: System.String [FromQueryAttribute], handler: SmartSentinelEye.Identity.Application.Commands.Handlers.DisableKioskCommandHandler [FromServicesAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods DELETE
          authorize policy=sse.identity.kiosks.write roles= schemes=
          tags IdentityKiosks
          name DisableKiosk
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Disable an enrolled kiosk. Required scope: sse.identity.kiosks.write
          produces 200 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /kiosks/
          handler List(fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], handler: SmartSentinelEye.Identity.Application.Queries.Handlers.ListKiosksQueryHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          authorize policy=sse.identity.kiosks.read roles= schemes=
          tags IdentityKiosks
          name ListKiosks
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List enrolled kiosks in your fabs. Omit fabId to span all of them; name one to narrow. Required scope: sse.identity.kiosks.read
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.Identity.Application.DTOs.RegisteredClientSummaryDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /webhook-integrations/{name}/rotate
          handler Rotate(name: System.String [], body: SmartSentinelEye.Identity.Api.Requests.RotateWebhookClientRequest [FromBodyAttribute], request: Microsoft.AspNetCore.Http.HttpRequest [], services: SmartSentinelEye.Identity.Api.WebhookRotationEndpoints+RotateWebhookServices [AsParametersAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          authorize policy=sse.webhooks.write roles= schemes=
          tags IdentityWebhookRotation
          accepts SmartSentinelEye.Identity.Api.Requests.RotateWebhookClientRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          name RotateWebhookClient
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Rotate a webhook integration's bearer onto a Keycloak JWT. Send If-Match with the version from GET /webhook-integrations to rotate an existing client, or If-None-Match: * to create one. Required scope: sse.webhooks.write
          produces 200 SmartSentinelEye.Identity.Application.DTOs.WebhookClientCredentialsDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 412 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 502 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /webhook-integrations/
          handler List(fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], handler: SmartSentinelEye.Identity.Application.Queries.Handlers.ListWebhookClientsQueryHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          authorize policy=sse.webhooks.write roles= schemes=
          tags IdentityWebhookRotation
          name ListWebhookClients
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List webhook service-account clients and the version each must be rotated at. Required scope: sse.webhooks.write
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.Identity.Application.DTOs.RegisteredClientSummaryDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        """;

    [Fact]
    public async Task The_identity_route_table_is_unchanged()
    {
        await using var host = new IdentityEndpointHost();

        var rendered = Normalise(string.Join("\n", host.Endpoints.Select(Render)));

        rendered.ShouldBe(Normalise(Baseline));
    }

    [Fact]
    public async Task The_identity_route_table_has_eight_routes()
    {
        await using var host = new IdentityEndpointHost();

        host.Endpoints.Count().ShouldBe(8);
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
    /// Some metadata is a compiler or binder artefact rather than contract: the async
    /// state-machine marker and the debugger-step marker Debug builds add to async methods (both follow
    /// whether a handler body awaits), and the per-parameter
    /// binding record (it is derived from the parameter list, which is rendered in full).
    /// </summary>
    private static bool IsContract(object item) =>
        item.GetType().Name != "ParameterBindingMetadata"
        && item is not AsyncStateMachineAttribute
        && item is not System.Diagnostics.DebuggerStepThroughAttribute;

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
