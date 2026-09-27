using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Issue #2358, spec 274 US1 — a variable literally named <c>snapshot</c> or
/// <c>resolve</c> is readable by name like any other variable. Before the
/// fix, <c>GET /system-variables/{name}</c> never runs for either literal:
/// ASP.NET Core's endpoint routing always prefers the literal
/// <c>/system-variables/snapshot</c> / <c>/system-variables/resolve</c>
/// routes over the parameterized <c>/system-variables/{name}</c>, so the
/// request lands on <see cref="SmartSentinelEye.SystemVariables.Api.SystemVariableEndpoints"/>'s
/// snapshot/resolve handlers instead and answers 400 (missing
/// <c>overlayIdentifier</c> / empty <c>text</c>) rather than the variable.
///
/// <para>
/// <b>The name cannot be uniquified</b> — the literal itself is the subject
/// under test, unlike every other suite's <see cref="VariableRequests.UniqueName"/>.
/// A previous, interrupted run may have left the variable <c>Defined</c>, so
/// defining tolerates <c>409</c> as well as <c>201</c>, and the test archives
/// it afterwards so reruns stay clean (a released name is free for reuse,
/// <c>GetVariableQueryHandler:19</c>).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class ShadowedVariableNameIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public Task InitializeAsync() => aspire.ResetSystemVariablesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string> FormerLiteralRouteSegments() => ["snapshot", "resolve"];

    [Theory]
    [MemberData(nameof(FormerLiteralRouteSegments))]
    public async Task A_variable_named_after_a_former_literal_route_segment_is_readable_by_name(string name)
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

        try
        {
            (await DefineIfAbsentAsync(variables, name)).Dispose();

            HttpResponseMessage fetched = await variables.GetAsync($"/system-variables/{name}?fabId=munich");
            string body = await fetched.Content.ReadAsStringAsync();
            fetched.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                $"GET /system-variables/{name} did not answer 200 with the variable itself — the literal "
                + $"route '/{name}' shadows the parameterized GET /system-variables/{{name}} (issue #2358). "
                + $"Response: {body}");

            JsonElement payload = JsonDocument.Parse(body).RootElement;
            payload.GetProperty("name").GetString().ShouldBe(name);
            fetched.Headers.ETag.ShouldNotBeNull(
                "a successful GET /system-variables/{name} response carries the concurrency ETag (ADR-0113), "
                + "which the snapshot/resolve handlers never set — its absence is itself evidence of the wrong handler.");
        }
        finally
        {
            // Released, not left behind: a name this test can't uniquify would
            // otherwise collide with the very next run.
            await VariableRequests.ArchiveAsync(variables, name);
        }
    }

    /// <summary>
    /// 201 on a clean database, 409 if a previous, interrupted run of this
    /// same theory left the name <c>Defined</c>. Either way the variable
    /// exists afterwards, which is all this test needs.
    /// </summary>
    private static async Task<HttpResponseMessage> DefineIfAbsentAsync(HttpClient variables, string name)
    {
        HttpResponseMessage defined = await variables.PostAsJsonAsync("/system-variables?fabId=munich", new
        {
            name,
            type = "String",
            initialValue = name,
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        });

        if (defined.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict)
        {
            return defined;
        }

        throw new InvalidOperationException(
            $"Defining '{name}' answered {defined.StatusCode}, neither 201 (fresh) nor 409 (left over from a "
            + $"previous run): {await defined.Content.ReadAsStringAsync()}");
    }
}
