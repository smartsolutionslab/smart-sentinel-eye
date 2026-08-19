using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 019 T020 — SC-003 and SC-004. An event that cannot be stored is refused
/// rather than accepted and discarded.
///
/// <para>
/// <c>hamburg</c> is the fixture: a fab that exists in the realm, gets its
/// partition from provisioning like any other, and has that partition dropped
/// here to recreate the state the whole feature is about. It is restored
/// afterwards, and no other test in this collection uses it.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class FabStorageRefusalIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string HamburgOperator = "op-hamburg@hamburg.test";
    private const string MunichOperator = "op-3@munich.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Puts hamburg's storage back, so a later run of this collection sees the
    /// stack it expected. Provisioning would restore it on the next migration
    /// pass anyway; this keeps the fixture honest in the meantime.
    /// </summary>
    public async Task DisposeAsync()
    {
        DateTime now = DateTime.UtcNow;
        DateTime nextMonth = now.AddMonths(1);

        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS events_hamburg PARTITION OF events
                FOR VALUES IN ('hamburg')
                PARTITION BY RANGE (ingested_at);
            """);

        // The months too, not just the fab partition. A fab partition with
        // nothing beneath it accepts no row — restoring only the parent would
        // leave hamburg in the very state this test manufactures, which is a
        // dirty fixture rather than a restored one.
        // Interpolated deliberately: these are identifiers and range bounds, and
        // Postgres parameterises neither. Every value is invariant-formatted off
        // DateTime.UtcNow — nothing here comes from outside this method.
        string monthly =
            $"""
            CREATE TABLE IF NOT EXISTS "events_hamburg_{now:yyyyMM}" PARTITION OF events_hamburg
                FOR VALUES FROM ('{now:yyyy-MM}-01') TO ('{nextMonth:yyyy-MM}-01');
            """;
#pragma warning disable EF1002
        await database.Database.ExecuteSqlRawAsync(monthly);
#pragma warning restore EF1002
    }

    [Fact]
    public async Task An_operator_of_a_fab_with_no_storage_is_refused_and_nothing_is_enqueued()
    {
        string kind = $"Refused{Guid.CreateVersion7():N}"[..20];

        await DropHamburgStorageAsync();

        using HttpClient hamburg = await ClientFor(HamburgOperator);
        HttpResponseMessage refused = await PostUntilRefusedAsync(hamburg, kind);

        refused.StatusCode.ShouldBe(
            HttpStatusCode.ServiceUnavailable, await refused.Content.ReadAsStringAsync());

        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("EVENT_FAB_NOT_PROVISIONED");

        // The assertion that matters. A 503 that had already written to the
        // channel would be the same defect with a better error message: the
        // event would land, late, while the caller was told it had not.
        await Task.Delay(TimeSpan.FromSeconds(3));
        (await HamburgEventCountAsync()).ShouldBe(
            0, "a refused write reached the ingest channel anyway");
    }

    /// <summary>FR-009 — the machine path is refused identically.</summary>
    [Fact]
    public async Task A_webhook_delivery_for_a_fab_with_no_storage_is_refused()
    {
        await DropHamburgStorageAsync();

        string name = $"refusal-{Guid.NewGuid():N}"[..20];
        using HttpClient hamburg = await ClientFor(HamburgOperator);

        HttpResponseMessage created = await hamburg.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        JsonElement body = await created.Content.ReadFromJsonAsync<JsonElement>();
        string token = body.GetProperty("token").GetString()!;

        HttpResponseMessage refused = await PostWebhookUntilRefusedAsync(name, token);

        refused.StatusCode.ShouldBe(
            HttpStatusCode.ServiceUnavailable, await refused.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// And a fab whose storage exists is untouched — the refusal is about
    /// storage, not about tightening the write path in general.
    /// </summary>
    [Fact]
    public async Task A_fab_with_storage_still_accepts_events()
    {
        using HttpClient munich = await ClientFor(MunichOperator);

        HttpResponseMessage created = await munich.PostAsJsonAsync("/events/manual", Body("Unaffected"));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    // ---- helpers ------------------------------------------------------------

    /// <summary>
    /// Polls until the refusal appears. The readiness cache is allowed to serve
    /// a stale <em>positive</em> for its TTL — a deliberate asymmetry, since a
    /// wrong "yes" costs one logged envelope while a wrong "no" would refuse a
    /// fab that can store perfectly well. So a partition dropped underneath a
    /// warm cache takes up to that TTL to be noticed, and this waits for it
    /// rather than pretending the window does not exist.
    /// </summary>
    /// <summary>
    /// Polls until the <em>provisioning</em> refusal appears, not merely until
    /// some refusal does.
    ///
    /// <para>
    /// Since spec 020 the write is synchronous, so a caller who slips past the
    /// stale-positive cache is refused by the write itself with
    /// <c>EVENT_NOT_STORED</c> — a 503 that arrives sooner and says less. That
    /// is a better answer than the 202 it replaced and it is not the one this
    /// test is about: FR-007 is that the refusal eventually <b>names its
    /// cause</b>, so the earlier, vaguer 503 is treated as "not yet".
    /// </para>
    /// </summary>
    private static async Task<HttpResponseMessage> PostUntilRefusedAsync(HttpClient client, string kind)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        HttpResponseMessage response = await client.PostAsJsonAsync("/events/manual", Body(kind));

        while (!await NamesTheMissingStorageAsync(response) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            response = await client.PostAsJsonAsync("/events/manual", Body(kind));
        }

        return response;
    }

    /// <summary>
    /// Reads the body as a string rather than deserialising it. The caller reads
    /// the same response again — for its own assertion and its failure message —
    /// and <c>ReadFromJsonAsync</c> consumes the stream, so peeking at the title
    /// here left the test failing on a closed stream instead of on what it was
    /// testing.
    /// </summary>
    private static async Task<bool> NamesTheMissingStorageAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
        {
            return false;
        }

        string body = await response.Content.ReadAsStringAsync();
        return body.Contains("EVENT_FAB_NOT_PROVISIONED", StringComparison.Ordinal);
    }

    /// <summary>
    /// Same stopping rule as the manual path, for the same reason: a bare 503
    /// is now also what a failed write answers, so accepting the first one
    /// would let this stop proving that the machine path names its cause.
    /// </summary>
    private async Task<HttpResponseMessage> PostWebhookUntilRefusedAsync(string name, string token)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        HttpResponseMessage response = await SendWebhookAsync(name, token);

        while (!await NamesTheMissingStorageAsync(response) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            response = await SendWebhookAsync(name, token);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendWebhookAsync(string name, string token)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post, $"/events/webhook/{name}?fabId=hamburg")
        {
            Content = JsonContent.Create(new { payload = new { severity = "high" } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await aspire.EventIngestion.SendAsync(request);
    }

    private async Task DropHamburgStorageAsync()
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        await database.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS events_hamburg;");
    }

    private async Task<long> HamburgEventCountAsync()
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM events WHERE fab_id = 'hamburg'")
            .SingleAsync();
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync("event-ingestion", username, OperatorPassword);

    private static object Body(string kind) => new
    {
        deviceId = "refusal-device",
        kind,
        occurredAt = DateTimeOffset.UtcNow,
        payload = new { note = "spec 019 US2" },
    };
}
