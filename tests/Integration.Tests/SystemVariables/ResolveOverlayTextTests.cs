using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;
using VariableAggregate = SmartSentinelEye.SystemVariables.Domain.Variable.Variable;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 148 T008 — <c>GET /system-variables/resolve</c> over the real stack
/// (ADR-0103: a new endpoint gets an integration test proving it answers
/// correctly through the real stack, not unit tests alone).
///
/// <para>
/// <b>Phase 4a — RED, and the route does not merely 404.</b> The endpoint is
/// not mapped yet (T007, backend-engineer, phase 4b). Naively that would mean
/// every request here gets a plain "no endpoint matched" 404. It does not:
/// <c>/system-variables/resolve</c> collides at the routing layer with the
/// already-mapped <c>GET /system-variables/{name}</c> (<c>GetOne</c>), because
/// "resolve" is itself a well-formed variable name
/// (<c>[A-Za-z][A-Za-z0-9_]{0,63}</c>) and there is no more specific literal
/// route registered to prefer over the parameterized one. So every request
/// below is actually answered by <c>GetOne</c>, looking up a variable
/// literally named "resolve" — which <see cref="InitializeAsync"/>'s reset
/// guarantees does not exist.
/// </para>
///
/// <para>
/// <b>What that means per scenario, stated rather than assumed.</b> The four
/// facts that expect <c>200</c> with a <c>ResolvedTextPreviewDto</c> body
/// (substitution, the unknown-name literal, the whitespace-grammar literal,
/// fab scoping) are genuinely red: <c>GetOne</c> answers <c>404
/// VARIABLE_NOT_FOUND</c> with a <c>VariableDto</c>-shaped body, never a
/// <c>resolvedText</c> field, so the assertion fails for a real reason. The
/// two auth facts are <b>not</b> exercising anything new — the group's bare
/// <c>.RequireAuthorization()</c> already 401s an anonymous caller on any
/// route in this group, and <c>GetOne</c> already requires the identical
/// <c>sse.variables.read</c> scope the real endpoint will declare — so both
/// pass today by coincidence of the collision, not because T007 did
/// anything. They are kept anyway as the regression guard for T007: once the
/// real route is mapped, these two facts must still hold, this time for the
/// endpoint's own declared authorization rather than borrowed from
/// <c>GetOne</c>.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class ResolveOverlayTextTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string ServiceAccountClientId = "scenario-simulator";
    private const string ServiceAccountSecret = "dev-only-scenario-simulator-secret";

    public Task InitializeAsync() => aspire.ResetSystemVariablesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_resolvable_placeholder_is_substituted()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = VariableRequests.UniqueName();
        await DefineNumberAsync(variables, name, "23.4");

        (HttpStatusCode status, string body) = await ResolveAsync(variables, $"{{{{{name}}}}}");

        status.ShouldBe(
            HttpStatusCode.OK,
            $"spec 148 T007 is not implemented — /resolve currently binds to GetOne's {{name}} route "
            + $"and answers as a lookup for a variable literally named 'resolve'. Actual: {status} {body}");
        body.ShouldContain("\"resolvedText\":\"23.4\"", customMessage: body);
    }

    /// <summary>FR-011 — an unknown name keeps its literal placeholder rather than vanishing.</summary>
    [Fact]
    public async Task An_unknown_name_keeps_the_literal_placeholder()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string unknown = VariableRequests.UniqueName();

        (HttpStatusCode status, string body) = await ResolveAsync(variables, $"{{{{{unknown}}}}}");

        status.ShouldBe(HttpStatusCode.OK, $"spec 148 T007 is not implemented. Actual: {status} {body}");
        body.ShouldContain($"\"resolvedText\":\"{{{{{unknown}}}}}\"", customMessage: body);
    }

    /// <summary>
    /// US4 — whitespace inside the braces is not a placeholder at all
    /// (grammar admits no whitespace); it is permanently unresolvable, unlike
    /// a genuinely unknown name. Pinned at the HTTP boundary, not only in
    /// <c>PlaceholderParserTests</c>, so a grammar regression there is caught
    /// on the wire too.
    /// </summary>
    [Fact]
    public async Task Whitespace_inside_the_braces_never_resolves()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        const string text = "{{ temperature }}";

        (HttpStatusCode status, string body) = await ResolveAsync(variables, text);

        status.ShouldBe(HttpStatusCode.OK, $"spec 148 T007 is not implemented. Actual: {status} {body}");
        body.ShouldContain($"\"resolvedText\":\"{text}\"", customMessage: body);
    }

    /// <summary>
    /// ADR-0115 — a name defined only in another fab is not this caller's to
    /// resolve; it renders the same as a name that exists nowhere. Seeded
    /// through the DbContext, mirroring <c>CrossFabVariableIntegrationTests</c>
    /// — the admin caller resolves to munich, so a dresden-only row is
    /// unreachable to it regardless of the endpoint under test.
    /// </summary>
    [Fact]
    public async Task A_name_defined_only_in_another_fab_is_not_resolved()
    {
        string name = VariableRequests.UniqueName();
        await SeedDresdenNumberAsync(name, 7);

        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

        (HttpStatusCode status, string body) = await ResolveAsync(variables, $"{{{{{name}}}}}");

        status.ShouldBe(HttpStatusCode.OK, $"spec 148 T007 is not implemented. Actual: {status} {body}");
        body.ShouldContain($"\"resolvedText\":\"{{{{{name}}}}}\"", customMessage: body);
    }

    /// <summary>
    /// Not a fact about T007 — the group's bare <c>.RequireAuthorization()</c>
    /// already refuses an anonymous caller on any route here, this one
    /// included. Kept as the regression guard: once the real route lands, an
    /// anonymous caller must still get 401 from its own declared
    /// authorization, not merely because the collision borrowed GetOne's.
    /// </summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        using HttpClient anonymous = aspire.CreateServiceClient("system-variables");

        (HttpStatusCode status, string body) = await ResolveAsync(anonymous, "{{temperature}}");

        status.ShouldBe(HttpStatusCode.Unauthorized, $"actual: {status} {body}");
    }

    /// <summary>
    /// Not a fact about T007 either — <c>GetOne</c> already requires the
    /// identical <c>sse.variables.read</c> scope spec 148 declares for the
    /// real endpoint, so this passes today by coincidence of the collision.
    /// Kept as the regression guard for the same reason as the anonymous
    /// case above.
    /// </summary>
    [Fact]
    public async Task A_caller_without_the_read_scope_is_refused()
    {
        using HttpClient unscoped = await ServiceAccountClientAsync();

        (HttpStatusCode status, string body) = await ResolveAsync(unscoped, "{{temperature}}");

        status.ShouldBe(HttpStatusCode.Forbidden, $"actual: {status} {body}");
    }

    private static async Task<(HttpStatusCode Status, string Body)> ResolveAsync(HttpClient client, string text)
    {
        HttpResponseMessage response =
            await client.GetAsync($"/system-variables/resolve?text={Uri.EscapeDataString(text)}");
        string body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body);
    }

    private static async Task DefineNumberAsync(HttpClient variables, string name, string initialValue)
    {
        (await variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Number",
            initialValue,
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        })).EnsureSuccessStatusCode();
    }

    private async Task SeedDresdenNumberAsync(string name, double value)
    {
        await using SystemVariablesDbContext context = await aspire.CreateSystemVariablesDbContextAsync();

        SystemClock clock = new();
        OperatorIdentifier author = OperatorIdentifier.From(Guid.CreateVersion7());
        VariableAggregate variable = VariableAggregate.Define(
            FabIdentifier.From("dresden"),
            VariableName.From(name),
            VariableType.Number,
            new VariableValue.NumberValue(value),
            booleanLabels: null,
            author,
            clock);
        variable.ClearPendingEvents();

        context.Variables.Add(variable);
        await context.SaveChangesAsync();
    }

    private async Task<HttpClient> ServiceAccountClientAsync()
    {
        HttpClient client = aspire.CreateServiceClient("system-variables");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ServiceAccountTokenAsync());
        return client;
    }

    /// <summary>Mirrors <c>VariableReadScopeIntegrationTests.ServiceAccountTokenAsync</c>.</summary>
    private async Task<string> ServiceAccountTokenAsync()
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();

        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ServiceAccountClientId,
            ["client_secret"] = ServiceAccountSecret,
        });

        HttpResponseMessage response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);
        response.EnsureSuccessStatusCode();

        System.Text.Json.JsonElement payload = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return payload.GetProperty("access_token").GetString()!;
    }
}
