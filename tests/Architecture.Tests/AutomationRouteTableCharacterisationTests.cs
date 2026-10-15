using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins the Automation rule route table this spec touches as it stands (spec 367, #2932, FR-002): every
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
public class AutomationRouteTableCharacterisationTests
{
    private const string Baseline = """
        /rules/
          handler Create(body: SmartSentinelEye.Automation.Api.Requests.CreateRuleRequest [FromBodyAttribute], services: SmartSentinelEye.Automation.Api.RulesEndpoints+CreateRuleServices [AsParametersAttribute], http: Microsoft.AspNetCore.Http.HttpContext [], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods POST
          authorize policy=sse.rules.write roles= schemes=
          tags Rules
          accepts SmartSentinelEye.Automation.Api.Requests.CreateRuleRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          name CreateRule
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Author a rule. Omit fabId when you belong to exactly one fab; name it when you belong to several (ADR-0114). Required scope: sse.rules.write
          produces 201 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Http.HttpValidationProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /rules/{name}/publish
          handler Publish(name: System.String [], request: Microsoft.AspNetCore.Http.HttpRequest [], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], handler: SmartSentinelEye.Automation.Application.Commands.Handlers.PublishRuleCommandHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods POST
          authorize policy=sse.rules.write roles= schemes=
          tags Rules
          name PublishRule
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Publish a rule. Requires If-Match with the rule's current version. Required scope: sse.rules.write
          produces 200 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /rules/{name}/archive
          handler Archive(name: System.String [], request: Microsoft.AspNetCore.Http.HttpRequest [], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], handler: SmartSentinelEye.Automation.Application.Commands.Handlers.ArchiveRuleCommandHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods POST
          authorize policy=sse.rules.write roles= schemes=
          tags Rules
          name ArchiveRule
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Archive a rule. Requires If-Match with the rule's current version. Required scope: sse.rules.write
          produces 200 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /rules/
          handler List(state: System.String [FromQueryAttribute,NullableAttribute], triggerSource: System.String [FromQueryAttribute,NullableAttribute], triggerKind: System.String [FromQueryAttribute,NullableAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], handler: SmartSentinelEye.Automation.Application.Queries.Handlers.ListRulesQueryHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          authorize policy=sse.rules.read roles= schemes=
          tags Rules
          name ListRules
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List rules in the fabs you are assigned to. Required scope: sse.rules.read
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.Automation.Application.DTOs.RuleDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /rules/{name}
          handler GetOne(name: System.String [], response: Microsoft.AspNetCore.Http.HttpResponse [], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], handler: SmartSentinelEye.Automation.Application.Queries.Handlers.GetRuleQueryHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods GET
          authorize policy=sse.rules.read roles= schemes=
          tags Rules
          name GetRule
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Read one rule by name, within the fabs you are assigned to. Required scope: sse.rules.read
          produces 200 SmartSentinelEye.Automation.Application.DTOs.RuleDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /rules/{name}/dry-run
          handler DryRun(name: System.String [], body: SmartSentinelEye.Automation.Api.Requests.DryRunRuleRequest [FromBodyAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], handler: SmartSentinelEye.Automation.Application.Queries.Handlers.DryRunRuleQueryHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [], fabId: System.String = '' [FromQueryAttribute,OptionalAttribute])
          methods POST
          authorize policy=sse.rules.read roles= schemes=
          tags Rules
          accepts SmartSentinelEye.Automation.Api.Requests.DryRunRuleRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          name DryRunRule
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Evaluate a rule against a sample event. A POST because it carries a body, but a read: nothing is persisted and no integration event is published. Required scope: sse.rules.read
          produces 200 SmartSentinelEye.Automation.Application.DTOs.DryRunResultDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        """;

    [Fact]
    public async Task The_automation_route_table_is_unchanged()
    {
        await using var host = new AutomationEndpointHost();

        var rendered = Normalise(string.Join("\n", host.Endpoints.Select(Render)));

        rendered.ShouldBe(Normalise(Baseline));
    }

    [Fact]
    public async Task The_automation_route_table_has_six_routes()
    {
        await using var host = new AutomationEndpointHost();

        host.Endpoints.Count().ShouldBe(6);
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
