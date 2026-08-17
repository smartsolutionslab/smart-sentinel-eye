using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 018 T008 — SC-003, the manipulation path closed.
///
/// <para>
/// Before this feature a Dresden operator could <c>POST
/// /events/manual?fabId=munich</c> and get <b>202 Accepted</b>: the event was
/// filed against Munich, where it drives Munich's automation rules and changes
/// what Munich's operators see. It is the only path in the product by which
/// one fab alters another's state, and it is what these cases close.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class ManualIngestFabScopingIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The inference, checked where it lands rather than by its status code.
    ///
    /// <para>
    /// <b>202 is not the assertion.</b> The inferred branch of
    /// <c>FabResolution.ResolveForWriteAsync</c> never calls
    /// <c>EnsureAccessAsync</c> — it has nothing to check, having derived the
    /// fab from the caller's own single group — so an inference that fell back
    /// to the <c>munich</c> default would return 202 just the same and this
    /// case would pass while the regression it exists to catch went through.
    /// The event has to be read back out of dresden.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_single_fab_operator_has_dresden_inferred_not_the_default()
    {
        // Run-unique: a fixed literal cannot tell this run's event from one a
        // previous run left in the same database.
        string kind = $"Inferred{Guid.NewGuid():N}"[..24];

        using HttpClient events = await ClientFor(DresdenOperator);

        HttpResponseMessage accepted = await events.PostAsJsonAsync("/events/manual", Body(kind));
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await BodyAsync(accepted));

        using HttpClient reader = await ClientFor(MultiFabOperator);
        (await WaitForCountAsync(reader, "dresden", kind)).ShouldBe(
            1, "the inferred fab did not land in dresden");

        // And not in munich, which is what everything else in the system
        // defaults to. Checked without waiting: the event has already been seen
        // to land, so if it had gone to munich instead it would be there now.
        (await CountAsync(reader, "munich", kind)).ShouldBe(
            0, "a dresden operator's event was filed against munich");
    }

    [Fact]
    public async Task A_multi_fab_operator_naming_no_fab_is_refused()
    {
        using HttpClient events = await ClientFor(MultiFabOperator);

        HttpResponseMessage refused = await events.PostAsJsonAsync("/events/manual", Body("AmbiguousKind"));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("EVENT_FAB_REQUIRED");
    }

    [Fact]
    public async Task A_multi_fab_operator_naming_one_of_their_fabs_is_accepted()
    {
        using HttpClient events = await ClientFor(MultiFabOperator);

        HttpResponseMessage accepted = await events.PostAsJsonAsync(
            "/events/manual?fabId=dresden", Body("NamedKind"));

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await BodyAsync(accepted));
    }

    /// <summary>
    /// SC-003, and the assertion that matters: <b>403 is not enough</b>. A
    /// refusal that had already enqueued would place the event in Munich's
    /// stream while reporting it had been stopped, so this checks Munich's
    /// listing afterwards (FR-007).
    /// </summary>
    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_ingests_nothing()
    {
        string kind = $"Injected{Guid.NewGuid():N}"[..24];

        using HttpClient dresden = await ClientFor(DresdenOperator);
        HttpResponseMessage refused = await dresden.PostAsJsonAsync(
            "/events/manual?fabId=munich", Body(kind));

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));

        // The ingest channel is asynchronous, so give a successful write time
        // to land before concluding nothing did.
        await Task.Delay(TimeSpan.FromSeconds(3));

        using HttpClient munichReader = await ClientFor(MultiFabOperator);
        HttpResponseMessage listed = await munichReader.GetAsync($"/events?fabId=munich&kind={kind}");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(listed));

        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("items").GetArrayLength().ShouldBe(
            0, "a refused write reached munich's stream anyway");
    }

    /// <summary>
    /// The number of events of this kind in the given fab, once the
    /// asynchronous ingest has had a chance to land. Polls up to 30 s while the
    /// count is zero and settles for a moment before reporting zero, so "it did
    /// not arrive" is a conclusion rather than a race.
    /// </summary>
    private static async Task<int> WaitForCountAsync(HttpClient reader, string fab, string kind)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            int count = await CountAsync(reader, fab, kind);
            if (count > 0 || DateTime.UtcNow >= deadline)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    private static async Task<int> CountAsync(HttpClient reader, string fab, string kind)
    {
        HttpResponseMessage listed = await reader.GetAsync($"/events?fabId={fab}&kind={kind}");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();
        return page.GetProperty("items").GetArrayLength();
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync("event-ingestion", username, OperatorPassword);

    private static object Body(string kind) => new
    {
        deviceId = "scoping-device",
        kind,
        occurredAt = DateTimeOffset.UtcNow,
        payload = new { note = "spec 018 scoping" },
    };

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs("event-ingestion")}";
}
