using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 018 T015 — SC-001 and SC-002 over real HTTP with real Keycloak tokens.
///
/// <para>
/// The handler tests prove the queries filter on a set of fabs, but they pass
/// that set in themselves. This exercises the leg they stub, and the leg that
/// was actually missing: that the set comes from the caller's groups claim
/// rather than from the query string. Every one of these assertions was a
/// <c>200</c> before this feature.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EventFabScopingIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The leak itself. Before spec 018 this returned <c>200</c> with Munich's
    /// events in the body.
    /// </summary>
    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_is_refused()
    {
        using HttpClient events = await ClientFor(DresdenOperator);

        HttpResponseMessage refused = await events.GetAsync("/events?fabId=munich");

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));
    }

    [Fact]
    public async Task Omitting_the_fab_spans_the_callers_own_fabs_rather_than_failing()
    {
        using HttpClient events = await ClientFor(DresdenOperator);

        // Previously a 400: fabId was required. Making it optional is the
        // widening half of a change that is otherwise strictly narrowing.
        HttpResponseMessage listed = await events.GetAsync("/events");

        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(listed));
    }

    [Fact]
    public async Task A_multi_fab_operator_may_narrow_to_one_of_their_own_fabs()
    {
        using HttpClient events = await ClientFor(MultiFabOperator);

        HttpResponseMessage listed = await events.GetAsync("/events?fabId=munich");

        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(listed));
    }

    /// <summary>
    /// SC-001: a dresden operator's listing contains no munich event, whatever
    /// they ask for. Seeded through the manual endpoint as the multi-fab
    /// operator, who legitimately holds munich.
    /// </summary>
    [Fact]
    public async Task A_dresden_operator_never_sees_a_munich_event()
    {
        string kind = $"Seeded{Guid.NewGuid():N}"[..20];

        using HttpClient seeder = await ClientFor(MultiFabOperator);
        (await seeder.PostAsJsonAsync("/events/manual?fabId=munich", Body(kind)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // The ingest channel is asynchronous; wait for it to land, confirmed
        // through the operator who may legitimately see it.
        await WaitUntilVisibleAsync(seeder, "munich", kind);

        using HttpClient dresden = await ClientFor(DresdenOperator);
        HttpResponseMessage listed = await dresden.GetAsync($"/events?kind={kind}");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(listed));

        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("items").GetArrayLength().ShouldBe(
            0, "a munich event reached a dresden operator's listing");
    }

    /// <summary>
    /// SC-002 — compared field by field, not by status alone. A 404 whose body
    /// differed would let an operator confirm another plant's event exists.
    /// </summary>
    [Fact]
    public async Task Another_fabs_event_is_indistinguishable_from_one_that_never_existed()
    {
        string kind = $"Hidden{Guid.NewGuid():N}"[..20];

        using HttpClient seeder = await ClientFor(MultiFabOperator);
        (await seeder.PostAsJsonAsync("/events/manual?fabId=munich", Body(kind)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid munichEvent = await WaitUntilVisibleAsync(seeder, "munich", kind);

        Guid neverExisted = Guid.CreateVersion7();

        using HttpClient dresden = await ClientFor(DresdenOperator);
        HttpResponseMessage hidden = await dresden.GetAsync($"/events/{munichEvent}");
        HttpResponseMessage absent = await dresden.GetAsync($"/events/{neverExisted}");

        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(hidden));
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(absent));

        (await NormalisedProblemAsync(hidden, munichEvent))
            .ShouldBe(await NormalisedProblemAsync(absent, neverExisted));
    }

    // ---- helpers ------------------------------------------------------------

    /// <summary>Waits for an ingested event to appear, returning its identifier.</summary>
    private static async Task<Guid> WaitUntilVisibleAsync(HttpClient events, string fab, string kind)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage listed = await events.GetAsync($"/events?fabId={fab}&kind={kind}");
            if (listed.IsSuccessStatusCode)
            {
                JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();
                JsonElement items = page.GetProperty("items");
                if (items.GetArrayLength() > 0)
                {
                    return items[0].GetProperty("eventIdentifier").GetGuid();
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"Event of kind '{kind}' never appeared in fab '{fab}'.");
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync("event-ingestion", username, OperatorPassword);

    private static object Body(string kind) => new
    {
        deviceId = "scoping-device",
        kind,
        occurredAt = DateTimeOffset.UtcNow,
        payload = new { note = "spec 018 read scoping" },
    };

    /// <summary>
    /// Two things are normalised out and only two: <c>traceId</c>, which
    /// differs per request by design, and the event identifier the caller
    /// itself supplied, which the two requests cannot share.
    /// </summary>
    private static async Task<string> NormalisedProblemAsync(HttpResponseMessage response, Guid requested)
    {
        JsonNode problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem.AsObject().Remove("traceId");

        return problem.ToJsonString()
            .Replace(requested.ToString(), "<requested-event>", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs("event-ingestion")}";
}
