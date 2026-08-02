using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// First integration coverage for SystemVariables. Every other context had a
/// suite; this one had none, and none of its endpoints were exercised
/// end-to-end by anything except a gateway routing assertion.
///
/// <para>
/// That matters for spec 012: <c>PUT /{name}/value</c> is the cleanest
/// lost-update in the system — two operators set the same variable and the
/// last write wins, with no revision history to make the loss visible
/// afterwards. This establishes the baseline before the concurrency
/// behaviour lands on top of it, so a regression there is attributable.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class SystemVariableLifecycleIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetSystemVariablesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_defined_variable_is_readable_by_name()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = UniqueName();

        HttpResponseMessage defined = await DefineNumberAsync(variables, name, "41");
        defined.StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage fetched = await variables.GetAsync($"/system-variables/{name}");
        fetched.EnsureSuccessStatusCode();

        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("name").GetString().ShouldBe(name);
        payload.GetProperty("type").GetString().ShouldBe("Number");
        payload.GetProperty("value").GetString().ShouldBe("41");
        payload.GetProperty("state").GetString().ShouldBe("Defined");
    }

    [Fact]
    public async Task Setting_a_value_replaces_the_previous_one()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = UniqueName();
        (await DefineNumberAsync(variables, name, "1")).EnsureSuccessStatusCode();

        HttpResponseMessage updated = await variables.PutAsJsonAsync(
            $"/system-variables/{name}/value", new { value = "99" });
        updated.EnsureSuccessStatusCode();

        JsonElement payload = await ReadAsync(variables, name);
        payload.GetProperty("value").GetString().ShouldBe("99");
    }

    [Fact]
    public async Task A_duplicate_name_is_refused_with_409()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = UniqueName();
        (await DefineNumberAsync(variables, name, "1")).EnsureSuccessStatusCode();

        HttpResponseMessage duplicate = await DefineNumberAsync(variables, name, "2");

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_value_that_does_not_match_the_declared_type_is_refused_with_400()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = UniqueName();
        (await DefineNumberAsync(variables, name, "1")).EnsureSuccessStatusCode();

        HttpResponseMessage refused = await variables.PutAsJsonAsync(
            $"/system-variables/{name}/value", new { value = "not-a-number" });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Archiving_moves_the_variable_out_of_Active()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = UniqueName();
        (await DefineNumberAsync(variables, name, "1")).EnsureSuccessStatusCode();

        HttpResponseMessage archived = await variables.PostAsync(
            $"/system-variables/{name}/archive", content: null);
        archived.EnsureSuccessStatusCode();

        JsonElement payload = await ReadAsync(variables, name);
        payload.GetProperty("state").GetString().ShouldBe("Archived");
    }

    [Fact]
    public async Task An_unknown_variable_reads_as_404()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");

        HttpResponseMessage fetched = await variables.GetAsync($"/system-variables/{UniqueName()}");

        fetched.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_list_endpoint_returns_a_defined_variable()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string name = UniqueName();
        (await DefineNumberAsync(variables, name, "7")).EnsureSuccessStatusCode();

        HttpResponseMessage listed = await variables.GetAsync("/system-variables");
        listed.EnsureSuccessStatusCode();

        JsonElement payload = await listed.Content.ReadFromJsonAsync<JsonElement>();
        payload.EnumerateArray()
            .Select(row => row.GetProperty("name").GetString())
            .ShouldContain(name);
    }

    private static Task<HttpResponseMessage> DefineNumberAsync(HttpClient variables, string name, string initial) =>
        variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Number",
            initialValue = initial,
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        });

    private static async Task<JsonElement> ReadAsync(HttpClient variables, string name)
    {
        HttpResponseMessage fetched = await variables.GetAsync($"/system-variables/{name}");
        fetched.EnsureSuccessStatusCode();

        return await fetched.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Variable names are unique across the context and the fixture is shared,
    // so each test mints its own rather than relying on reset ordering.
    private static string UniqueName() => $"var{Guid.NewGuid():N}"[..16];
}
