using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Drives a mapped endpoint's <see cref="RequestDelegate"/> directly, with no server and no
/// authentication middleware, for the characterisation tests of spec 366 (#2931). It is the logic
/// of <c>LayoutCompositionEndpointHost.SendAsync</c> (spec 363) lifted out of its host so that
/// each context's host is only its own doubles and registrations.
///
/// <para>
/// The principal is set on the context, so scope policies are pinned by the route-table
/// baselines rather than here. No global exception handler runs either: a thrown exception is
/// what the delegate does, and is what a test pins.
/// </para>
/// </summary>
internal static class InProcessEndpointDriver
{
    public static IEnumerable<RouteEndpoint> Endpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>();

    public static ClaimsPrincipal Operator(Guid subject, params string[] fabs) =>
        new(new ClaimsIdentity(
            [
                new Claim("sub", subject.ToString()),
                .. fabs.Select(fab => new Claim("groups", "/fabs/" + fab)),
            ],
            "test"));

    /// <summary>
    /// Sends a request to the endpoint mapped at <paramref name="pattern"/> for
    /// <paramref name="method"/>, with the route values the template would have bound.
    /// </summary>
    public static async Task<InProcessResponse> SendAsync(
        WebApplication app,
        string method,
        string pattern,
        ClaimsPrincipal user,
        InProcessRequest request)
    {
        var endpoint = Endpoints(app).Single(candidate =>
            candidate.RoutePattern.RawText == pattern
            && candidate.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains(method));

        var context = new DefaultHttpContext { RequestServices = app.Services, User = user };
        context.Request.Method = method;
        foreach (var (name, value) in request.Route ?? new Dictionary<string, string>())
        {
            context.Request.RouteValues[name] = value;
        }

        foreach (var (name, value) in request.Headers ?? new Dictionary<string, string>())
        {
            context.Request.Headers[name] = value;
        }

        if (request.Query is not null)
        {
            context.Request.QueryString = new QueryString(request.Query);
        }

        if (request.BodyJson is not null)
        {
            var payload = Encoding.UTF8.GetBytes(request.BodyJson);
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(payload);
            context.Request.ContentLength = payload.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyPresent());
        }

        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var location = context.Response.Headers.Location.ToString();

        return new InProcessResponse(context.Response.StatusCode, await reader.ReadToEndAsync(), location);
    }

    /// <summary>
    /// A <see cref="DefaultHttpContext"/> has no server in front of it to say a request can carry
    /// a body, and without that the binder reports the body as not provided.
    /// </summary>
    private sealed class BodyPresent : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}

internal sealed record InProcessRequest(
    IReadOnlyDictionary<string, string>? Route = null,
    string? BodyJson = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Query = null);

internal sealed record InProcessResponse(int Status, string Body, string Location);
