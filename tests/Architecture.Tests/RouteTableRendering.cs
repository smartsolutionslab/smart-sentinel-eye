using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// The canonical text rendering of a mapped route table, shared by the route-table baselines of
/// spec 366 (#2931). It is spec 363's renderer, with one addition: the rate-limit policy name is
/// printed, because <c>whep-authorize</c> is a security control and a type name alone would not
/// notice it being swapped for another policy.
///
/// <para>
/// The OpenAPI document and the generated clients are derived from exactly this metadata, so a
/// baseline compared against it pins what a consumer can see. Attribute names within one
/// parameter are sorted because reflection does not promise an order.
/// </para>
/// </summary>
internal static class RouteTableRendering
{
    public static string RenderAll(IEnumerable<RouteEndpoint> endpoints) =>
        Normalise(string.Join("\n", endpoints.Select(Render)));

    public static string Normalise(string text) => text.Replace("\r\n", "\n").Trim();

    public static string Render(RouteEndpoint endpoint)
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
                EnableRateLimitingAttribute limit => "rate-limit " + limit.PolicyName,
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
