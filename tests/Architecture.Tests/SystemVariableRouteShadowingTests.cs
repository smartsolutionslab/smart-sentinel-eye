using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.SystemVariables.Api;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Issue #2358 — ASP.NET Core endpoint routing ranks a literal segment above
/// a parameter segment, so a literal route sharing a segment with an
/// unconstrained <c>{name}</c> route always wins. A variable that is legally
/// named after such a literal (<see cref="VariableName.From"/> does not
/// reject it) can be defined, set and archived, but never read back by
/// <c>GET /system-variables/{name}</c> — the read is silently swallowed by
/// the literal's own handler instead.
///
/// <para>
/// <b>The assertion is on the endpoints ASP.NET built, not on the text of
/// the file</b> — the same technique <see cref="SystemVariableReadScopeTests"/>
/// uses (<c>MapSystemVariableEndpoints</c> mapped in-process into a real
/// <see cref="WebApplication"/>, read back off <see cref="IEndpointRouteBuilder.DataSources"/>).
/// A source-text scan would miss a shadow introduced by registration order
/// or a convention added later; this cannot, because it walks the exact
/// <see cref="Microsoft.AspNetCore.Routing.Patterns.RoutePattern"/> the
/// framework resolved requests against.
/// </para>
///
/// <para>
/// <b>The guard states the invariant, not today's two names.</b> It pairs
/// every same-verb, same-segment-count route and flags any position where
/// one route has a literal and the other has an unconstrained parameter, so
/// a future <c>MapGet("/foo", ...)</c> alongside <c>MapGet("/{name}", ...)</c>
/// fails it exactly as <c>/snapshot</c> and <c>/resolve</c> do today.
/// </para>
/// </summary>
public class SystemVariableRouteShadowingTests
{
    [Fact]
    public void No_literal_route_segment_that_is_a_legal_variable_name_shares_a_position_with_an_unconstrained_name_parameter()
    {
        List<(string[] Verbs, string[] Segments)> endpoints = MappedEndpoints();

        List<string> shadowed = [];
        foreach ((string[] verbsA, string[] segmentsA) in endpoints)
        {
            foreach ((string[] verbsB, string[] segmentsB) in endpoints)
            {
                if (ReferenceEquals(segmentsA, segmentsB)
                    || segmentsA.Length != segmentsB.Length
                    || !verbsA.Intersect(verbsB, StringComparer.Ordinal).Any())
                {
                    continue;
                }

                for (int position = 0; position < segmentsA.Length; position++)
                {
                    if (IsLiteral(segmentsA[position])
                        && IsUnconstrainedParameter(segmentsB[position])
                        && SameElsewhere(segmentsA, segmentsB, position))
                    {
                        shadowed.Add(segmentsA[position]);
                    }
                }
            }
        }

        List<string> shadowedLegalNames = [.. shadowed.Distinct(StringComparer.Ordinal).Where(IsLegalVariableName)];

        shadowedLegalNames.ShouldBeEmpty(
            $"literal route segment(s) [{string.Join(", ", shadowedLegalNames)}] occupy the same position as an "
            + "unconstrained {name} parameter in a same-verb, same-length /system-variables route. ASP.NET Core "
            + "ranks a literal segment above a parameter segment, so GET /system-variables/<literal> always "
            + "reaches the literal's own handler, and since VariableName.From accepts every one of these as a "
            + "legal name, a variable defined with that name can never be read back by "
            + "GET /system-variables/{name} (issue #2358). Move the literal off the collision path (e.g. a "
            + "reserved '/-/' segment) or add a route constraint that excludes every legal VariableName.");
    }

    private static bool IsLegalVariableName(string candidate)
    {
        try
        {
            VariableName.From(candidate);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsLiteral(string segment) => !segment.StartsWith('{');

    /// <summary>
    /// A route parameter with no <c>:constraint</c> suffix — the shape that
    /// admits any string, including one that collides with a sibling
    /// literal. <c>{camera:guid}</c>-style constraints are excluded on
    /// purpose: they are how the other contexts avoid this exact defect
    /// (spec 274 §7).
    /// </summary>
    private static bool IsUnconstrainedParameter(string segment) =>
        segment.StartsWith('{') && segment.EndsWith('}') && !segment.Contains(':');

    private static bool SameElsewhere(string[] a, string[] b, int except)
    {
        for (int i = 0; i < a.Length; i++)
        {
            if (i != except && !string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<(string[] Verbs, string[] Segments)> MappedEndpoints()
    {
        WebApplication app = WebApplication.CreateBuilder([]).Build();
        app.MapSystemVariableEndpoints();

        List<(string[] Verbs, string[] Segments)> endpoints = [];
        foreach (Endpoint endpoint in ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints))
        {
            if (endpoint is not RouteEndpoint route)
            {
                continue;
            }

            string[] verbs = [.. endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []];
            endpoints.Add((verbs, SegmentsOf(route)));
        }

        return endpoints;
    }

    private static string[] SegmentsOf(RouteEndpoint route)
    {
        string raw = (route.RoutePattern.RawText ?? string.Empty).Trim('/');

        return raw.Length == 0 ? [] : raw.Split('/');
    }
}
