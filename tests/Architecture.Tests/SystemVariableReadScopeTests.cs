using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.SystemVariables.Api;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 073, issue 2070 — <c>sse.variables.read</c> is in the catalogue
/// (<see cref="Scope.Sse.Variables.Read"/>), is in the realm, and is granted by
/// default to <c>kiosk-web</c>, <c>kiosk-wall</c> and <c>management-web</c>.
/// Until this test nothing required it: the three reads on
/// <c>/system-variables</c> carried only the group's bare
/// <c>.RequireAuthorization()</c>, so any authenticated fab member reached them
/// whatever they were granted.
///
/// <para>
/// <b>The assertion is on the endpoint ASP.NET built, not on the text of the
/// file.</b> The endpoints are mapped in-process into a real
/// <see cref="WebApplication"/> and read back off the
/// <see cref="IEndpointRouteBuilder.DataSources"/> the framework populated, so
/// the metadata asserted here is the metadata the authorization middleware will
/// read at runtime. A source scan would pass on a
/// <c>.RequireAuthorization(...)</c> that a later convention overwrote; this
/// cannot.
/// </para>
///
/// <para>
/// The scope is taken from the constant rather than typed as a literal, so a
/// rename of the constant moves this guard with it instead of leaving it
/// asserting a string nothing enforces — which is the shape of the defect
/// itself.
/// </para>
/// </summary>
public class SystemVariableReadScopeTests
{
    private const string List = "/system-variables";
    private const string Snapshot = "/system-variables/snapshot";
    private const string One = "/system-variables/{name}";

    public static TheoryData<string> Reads() => [List, Snapshot, One];

    public static TheoryData<string, string> Writes()
    {
        TheoryData<string, string> data = [];
        data.Add("POST", "/system-variables");
        data.Add("PUT", "/system-variables/{name}/value");
        data.Add("POST", "/system-variables/{name}/archive");
        return data;
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public void Every_system_variable_read_enforces_the_variables_read_scope(string route)
    {
        string?[] policies = PoliciesOn("GET", route);

        policies.ShouldContain(
            Scope.Sse.Variables.Read,
            $"GET {route} does not enforce {Scope.Sse.Variables.Read}. "
            + $"The authorization metadata ASP.NET built for it names: "
            + $"[{string.Join(", ", policies.Select(policy => policy ?? "<no policy — authentication only>"))}]. "
            + "A bare .RequireAuthorization() admits any authenticated caller in any fab, whatever scopes "
            + "they hold, so the scope the realm grants three clients by name is enforced by nothing. "
            + "Chain .RequireAuthorization(Scope.Sse.Variables.Read) in the position the three writes "
            + "chain theirs; leave the group-level .RequireAuthorization() alone, since policies compose "
            + "by AND and dropping it changes what an unauthenticated caller gets.");
    }

    /// <summary>
    /// The control. Not the behaviour under change — it holds today and must go
    /// on holding — but without it a red above could equally mean this reader
    /// cannot see a policy at all. The writes prove the reader works.
    /// </summary>
    [Theory]
    [MemberData(nameof(Writes))]
    public void Every_system_variable_write_enforces_the_variables_write_scope(string verb, string route)
    {
        string?[] policies = PoliciesOn(verb, route);

        policies.ShouldContain(
            Scope.Sse.Variables.Write, $"{verb} {route} no longer enforces {Scope.Sse.Variables.Write}");
    }

    /// <summary>
    /// Every authorization policy name the framework attached to the endpoint
    /// for <paramref name="verb"/> <paramref name="route"/>. A bare
    /// <c>.RequireAuthorization()</c> contributes a <c>null</c> entry — it is an
    /// <see cref="IAuthorizeData"/> naming no policy — which is exactly what the
    /// three reads contribute today and why the message above prints it.
    /// </summary>
    private static string?[] PoliciesOn(string verb, string route)
    {
        Endpoint endpoint = MappedEndpoints()
            .Where(candidate => string.Equals(RouteOf(candidate), route, StringComparison.Ordinal))
            .FirstOrDefault(candidate => Verbs(candidate).Contains(verb, StringComparer.Ordinal))
            ?? throw new InvalidOperationException(
                $"MapSystemVariableEndpoints registered no {verb} {route}. Registered: "
                + string.Join(", ", MappedEndpoints().Select(each => $"{string.Join('|', Verbs(each))} {RouteOf(each)}")));

        return [.. endpoint.Metadata.OfType<IAuthorizeData>().Select(data => data.Policy)];
    }

    private static IReadOnlyList<Endpoint> MappedEndpoints()
    {
        WebApplication app = WebApplication.CreateBuilder([]).Build();
        app.MapSystemVariableEndpoints();

        return [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)];
    }

    /// <summary>
    /// <c>MapGroup("/system-variables")</c> plus <c>MapGet("/")</c> yields the
    /// raw pattern <c>/system-variables/</c>. The trailing slash is an artifact
    /// of composing the two, not part of the route a caller types.
    /// </summary>
    private static string RouteOf(Endpoint endpoint) =>
        endpoint is RouteEndpoint route
            ? route.RoutePattern.RawText?.TrimEnd('/') ?? string.Empty
            : string.Empty;

    private static IReadOnlyList<string> Verbs(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
}
