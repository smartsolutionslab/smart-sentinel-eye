using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;
using VariableAggregate = SmartSentinelEye.SystemVariables.Domain.Variable.Variable;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 148 T008 — <c>GET /system-variables/resolve</c> over the real stack
/// (ADR-0103: a new endpoint gets an integration test proving it answers
/// correctly through the real stack, not unit tests alone). Covers every
/// scenario <c>tasks.md:34</c> lists: the happy path plus Number/Boolean
/// rendering, all three unresolvable outcomes as <c>placeholders</c> rows
/// (not only <c>resolvedText</c>), fab scoping, both 400s, and both refusal
/// shapes (401 anonymous, 403 unscoped, 403 unheld fab).
/// </summary>
[Collection(AspireCollection.Name)]
public class ResolveOverlayTextTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string ServiceAccountClientId = "scenario-simulator";
    private const string ServiceAccountSecret = "dev-only-scenario-simulator-secret";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => aspire.ResetSystemVariablesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_resolvable_number_placeholder_is_substituted_and_reports_its_row()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = VariableRequests.UniqueName();
        // 23.4 does not round-trip exactly as a double: ToWireString() (G17)
        // renders "23.399999999999999", while Render() (what this endpoint
        // must use) renders "23.4". Picked deliberately so a regression back
        // to ToWireString() fails this assertion (plan.md risk 1).
        await DefineNumberAsync(variables, name, "23.4");

        JsonElement preview = await ResolveOkAsync(variables, $"{{{{{name}}}}}");

        preview.GetProperty("resolvedText").GetString().ShouldBe("23.4");
        JsonElement row = SinglePlaceholder(preview);
        row.GetProperty("name").GetString().ShouldBe(name);
        row.GetProperty("outcome").GetString().ShouldBe("Resolved");
        row.GetProperty("fab").GetString().ShouldBe("munich");
        row.GetProperty("renderedValue").GetString().ShouldBe("23.4");
    }

    /// <summary>
    /// The other half of decision 1: a Boolean's wire value is "true"/"false"
    /// (<c>ToWireString()</c>), but a resolved placeholder must show the
    /// variable's own labels (<c>Render()</c>) — asserted against custom
    /// labels so the default "Yes"/"No" can't coincidentally match.
    /// </summary>
    [Fact]
    public async Task A_resolvable_boolean_placeholder_renders_its_label_not_the_wire_value()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = VariableRequests.UniqueName();
        await DefineBooleanAsync(variables, name, "true", truthyLabel: "On", falsyLabel: "Off");

        JsonElement preview = await ResolveOkAsync(variables, $"{{{{{name}}}}}");

        preview.GetProperty("resolvedText").GetString().ShouldBe("On");
        JsonElement row = SinglePlaceholder(preview);
        row.GetProperty("outcome").GetString().ShouldBe("Resolved");
        row.GetProperty("fab").GetString().ShouldBe("munich");
        row.GetProperty("renderedValue").GetString().ShouldBe("On");
    }

    /// <summary>FR-011 — an unknown name keeps its literal placeholder rather than vanishing.</summary>
    [Fact]
    public async Task An_unknown_name_keeps_the_literal_placeholder_and_reports_the_unknown_outcome()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string unknown = VariableRequests.UniqueName();

        JsonElement preview = await ResolveOkAsync(variables, $"{{{{{unknown}}}}}");

        preview.GetProperty("resolvedText").GetString().ShouldBe($"{{{{{unknown}}}}}");
        JsonElement row = SinglePlaceholder(preview);
        row.GetProperty("outcome").GetString().ShouldBe("Unknown");
        row.GetProperty("fab").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("renderedValue").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// A defined-but-never-set variable is a different outcome than Unknown —
    /// distinguishing the two is spec.md's stated reason the
    /// <c>placeholders</c> array exists at all.
    /// </summary>
    [Fact]
    public async Task An_unset_placeholder_keeps_the_literal_placeholder_and_reports_the_unset_outcome()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = VariableRequests.UniqueName();
        await DefineUnsetNumberAsync(variables, name);

        JsonElement preview = await ResolveOkAsync(variables, $"{{{{{name}}}}}");

        preview.GetProperty("resolvedText").GetString().ShouldBe($"{{{{{name}}}}}");
        JsonElement row = SinglePlaceholder(preview);
        row.GetProperty("outcome").GetString().ShouldBe("Unset");
        // S2 — the fab it was found in, same as a Resolved row (plan.md:170).
        row.GetProperty("fab").GetString().ShouldBe("munich");
        row.GetProperty("renderedValue").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// BLOCKER 1 — the only real-database exercise of
    /// <c>VariableRepository.ExistsIncludingArchivedAsync</c>, the EF method
    /// that exists solely to make this outcome reachable.
    /// <c>GetByNameAsync</c> deliberately excludes archived rows (FR-005), so
    /// nothing else in this suite runs it.
    /// </summary>
    [Fact]
    public async Task An_archived_placeholder_keeps_the_literal_placeholder_and_reports_the_archived_outcome()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = VariableRequests.UniqueName();
        await DefineNumberAsync(variables, name, "1");
        (await VariableRequests.ArchiveAsync(variables, name)).EnsureSuccessStatusCode();

        JsonElement preview = await ResolveOkAsync(variables, $"{{{{{name}}}}}");

        preview.GetProperty("resolvedText").GetString().ShouldBe($"{{{{{name}}}}}");
        JsonElement row = SinglePlaceholder(preview);
        row.GetProperty("outcome").GetString().ShouldBe("Archived");
        // Archived stays null: it is the not-found path, and
        // ExistsArchivedInAnyFabAsync only ever returns a bool (S2).
        row.GetProperty("fab").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("renderedValue").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// US4 — whitespace inside the braces is not a placeholder at all
    /// (grammar admits no whitespace); it is permanently unresolvable, unlike
    /// a genuinely unknown name, and produces no <c>placeholders</c> row.
    /// Pinned at the HTTP boundary, not only in <c>PlaceholderParserTests</c>,
    /// so a grammar regression there is caught on the wire too.
    /// </summary>
    [Fact]
    public async Task Whitespace_inside_the_braces_never_resolves()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        const string text = "{{ temperature }}";

        JsonElement preview = await ResolveOkAsync(variables, text);

        preview.GetProperty("resolvedText").GetString().ShouldBe(text);
        preview.GetProperty("placeholders").GetArrayLength().ShouldBe(0);
    }

    /// <summary>
    /// ADR-0115 — a name defined only in another fab is not this caller's to
    /// resolve; it renders the same as a name that exists nowhere, and its
    /// row is Unknown, not Archived. Seeded through the DbContext, mirroring
    /// <c>CrossFabVariableIntegrationTests</c> — the admin caller resolves to
    /// munich, so a dresden-only row is unreachable to it regardless.
    /// </summary>
    [Fact]
    public async Task A_name_defined_only_in_another_fab_is_not_resolved()
    {
        string name = VariableRequests.UniqueName();
        await SeedDresdenNumberAsync(name, 7);

        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

        JsonElement preview = await ResolveOkAsync(variables, $"{{{{{name}}}}}");

        preview.GetProperty("resolvedText").GetString().ShouldBe($"{{{{{name}}}}}");
        JsonElement row = SinglePlaceholder(preview);
        row.GetProperty("outcome").GetString().ShouldBe("Unknown");
    }

    [Fact]
    public async Task Empty_text_is_refused()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

        HttpResponseMessage refused = await variables.GetAsync("/system-variables/resolve?text=");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("VARIABLE_INVALID_INPUT");
    }

    [Fact]
    public async Task Text_longer_than_256_characters_is_refused()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string overlong = new('a', 257);

        (HttpStatusCode status, string body) = await ResolveAsync(variables, overlong);

        status.ShouldBe(HttpStatusCode.BadRequest, body);
        JsonElement problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("title").GetString().ShouldBe("VARIABLE_INVALID_INPUT");
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        using HttpClient anonymous = aspire.CreateServiceClient("system-variables");

        (HttpStatusCode status, string body) = await ResolveAsync(anonymous, "{{temperature}}");

        status.ShouldBe(HttpStatusCode.Unauthorized, $"actual: {status} {body}");
    }

    [Fact]
    public async Task A_caller_without_the_read_scope_is_refused()
    {
        using HttpClient unscoped = await ServiceAccountClientAsync();

        (HttpStatusCode status, string body) = await ResolveAsync(unscoped, "{{temperature}}");

        status.ShouldBe(HttpStatusCode.Forbidden, $"actual: {status} {body}");
    }

    /// <summary>
    /// Naming a fab the caller does not hold refuses on
    /// <c>ResolveReadFabsAsync</c>'s own authorization check — a different
    /// code path than the missing-scope 403 above, and unexercised before
    /// this fact (spec 148 phase 6 blocker 2 item 5).
    /// </summary>
    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_is_refused()
    {
        using HttpClient variables =
            await aspire.CreateAuthenticatedClientAsync("system-variables", DresdenOperator, OperatorPassword);

        HttpResponseMessage refused =
            await variables.GetAsync("/system-variables/resolve?text=" + Uri.EscapeDataString("{{temperature}}") + "&fabId=munich");

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    private static async Task<(HttpStatusCode Status, string Body)> ResolveAsync(HttpClient client, string text)
    {
        HttpResponseMessage response =
            await client.GetAsync($"/system-variables/resolve?text={Uri.EscapeDataString(text)}");
        string body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body);
    }

    private static async Task<JsonElement> ResolveOkAsync(HttpClient client, string text)
    {
        HttpResponseMessage response =
            await client.GetAsync($"/system-variables/resolve?text={Uri.EscapeDataString(text)}");
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement;
    }

    private static JsonElement SinglePlaceholder(JsonElement preview)
    {
        JsonElement placeholders = preview.GetProperty("placeholders");
        placeholders.GetArrayLength().ShouldBe(1);
        return placeholders[0];
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

    private static async Task DefineUnsetNumberAsync(HttpClient variables, string name)
    {
        (await variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Number",
            initialValue = (string?)null,
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        })).EnsureSuccessStatusCode();
    }

    private static async Task DefineBooleanAsync(
        HttpClient variables, string name, string initialValue, string truthyLabel, string falsyLabel)
    {
        (await variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Boolean",
            initialValue,
            truthyLabel,
            falsyLabel,
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

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("access_token").GetString()!;
    }
}
