using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// A caller that defines variables can take them away again (#2004).
///
/// <para>
/// The audit-ingest measurement defines 51 variables per run and, against a
/// run-mode stack that is never torn down, kept every one of them: 1468 of the
/// 1559 rows on the dev stack were measurement residue. There is no delete
/// endpoint and that is deliberate — archiving is the removal the domain
/// offers — so what a run needs is a sweep that archives what it created.
/// </para>
///
/// <para>
/// <b>This covers the sweep, not the run's use of it.</b> Driving the real
/// measurement here would mean 1100 events and a three-minute wait for a
/// property that does not need them. What the measurement adds on top is one
/// call, and a run-mode run reports what it archived.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class VariableResidueCleanupTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetSystemVariablesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_swept_variable_is_archived_and_gone_from_the_listing()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string[] names = [UniqueName(), UniqueName(), UniqueName()];
        foreach (string name in names)
        {
            (await DefineAsync(variables, name)).EnsureSuccessStatusCode();
        }

        int archived = await VariableRequests.ArchiveAllAsync(variables, names, CancellationToken.None);

        archived.ShouldBe(names.Length);
        foreach (string name in names)
        {
            (await StateAsync(variables, name)).ShouldBe("Archived");
        }

        (await NamesAsync(variables)).ShouldNotContain(names[0]);
    }

    /// <summary>
    /// The sweep answers for every variable it was given, or its count is a
    /// number a caller cannot act on — "51 archived" from a run that left ten
    /// behind is worse than no figure at all.
    /// </summary>
    [Fact]
    public async Task A_variable_that_cannot_be_archived_fails_the_sweep_rather_than_shrinking_the_count()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        string defined = UniqueName();
        (await DefineAsync(variables, defined)).EnsureSuccessStatusCode();

        await Should.ThrowAsync<HttpRequestException>(() =>
            VariableRequests.ArchiveAllAsync(
                variables, [defined, UniqueName()], CancellationToken.None));
    }

    private static Task<HttpResponseMessage> DefineAsync(HttpClient variables, string name) =>
        variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Number",
            initialValue = "1",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        });

    private static async Task<string?> StateAsync(HttpClient variables, string name)
    {
        HttpResponseMessage fetched = await variables.GetAsync($"/system-variables/{name}");
        fetched.EnsureSuccessStatusCode();
        JsonElement payload = await fetched.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("state").GetString();
    }

    private static async Task<IReadOnlyList<string?>> NamesAsync(HttpClient variables)
    {
        HttpResponseMessage listed = await variables.GetAsync("/system-variables");
        listed.EnsureSuccessStatusCode();
        JsonElement payload = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return payload.EnumerateArray().Select(row => row.GetProperty("name").GetString()).ToArray();
    }

    private static string UniqueName() => $"res{Guid.NewGuid():N}"[..16];
}
