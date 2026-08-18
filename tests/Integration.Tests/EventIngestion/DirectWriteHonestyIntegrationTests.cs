using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 020 T016 — quickstart step 3. The direct write paths stopped saying
/// "accepted" and started saying what happened, so this asserts both halves of
/// that: a 201 that is true, and a failure the caller can see.
/// </summary>
[Collection(AspireCollection.Name)]
public class DirectWriteHonestyIntegrationTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string Fab = "hamburg";
    private const string Operator = "op-hamburg@hamburg.test";
    private const string OperatorPassword = "Operator1234";

    /// <summary>
    /// FR-001/FR-002. The <c>Location</c> is followed rather than parsed,
    /// because a 201 pointing at a 404 is the same lie in a better costume —
    /// and that is exactly what the old 202-then-buffer would have produced.
    /// </summary>
    [Fact]
    public async Task A_created_event_is_readable_at_the_location_it_reports()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", Operator, OperatorPassword);

        string kind = $"Direct{Guid.CreateVersion7():N}"[..20];
        HttpResponseMessage created = await client.PostAsJsonAsync("/events/manual", new
        {
            deviceId = "honesty-device",
            kind,
            occurredAt = DateTimeOffset.UtcNow,
            payload = new { note = "spec 020 T016" },
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location.ShouldNotBeNull("a 201 without a Location says nothing about where the event is");

        string location = created.Headers.Location.ToString();
        output.WriteLine($"POST /events/manual -> 201 {location}");

        // No delay. The point of storing before answering is that there is
        // nothing to wait for; a retry loop here would hide a regression to
        // eventual consistency behind a passing test.
        HttpResponseMessage fetched = await client.GetAsync(location);
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK, "the 201 pointed at nothing");

        JsonElement body = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("kind").GetString().ShouldBe(kind);
    }

    /// <summary>
    /// FR-002. The failure the caller used to be told nothing about. A 2xx here
    /// would mean the system had promised something it did not do, which is the
    /// whole defect.
    /// </summary>
    [Fact]
    public async Task A_write_that_cannot_be_stored_is_refused_and_stores_nothing()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", Operator, OperatorPassword);

        string kind = $"Refused{Guid.CreateVersion7():N}"[..20];
        await ExecuteAsync($"DROP TABLE IF EXISTS events_{Fab};");
        output.WriteLine($"dropped events_{Fab}");

        try
        {
            // Spec 019's readiness answer is cached, so the refusal only becomes
            // visible once that cache next looks. Waiting for it is the test
            // being honest about what the system promises, not padding.
            HttpResponseMessage refused = await RetryUntilRefusedAsync(client, kind);
            output.WriteLine($"POST /events/manual with storage away -> {(int)refused.StatusCode}");

            ((int)refused.StatusCode).ShouldBeGreaterThanOrEqualTo(500);
            (await CountAsync(kind)).ShouldBe(0, "a refused write stored the event anyway");
        }
        finally
        {
            await RestoreStorageAsync();
        }
    }

    private static async Task<HttpResponseMessage> RetryUntilRefusedAsync(HttpClient client, string kind)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(90);
        HttpResponseMessage response = null!;

        while (DateTimeOffset.UtcNow < deadline)
        {
            response = await client.PostAsJsonAsync("/events/manual", new
            {
                deviceId = "honesty-device",
                kind,
                occurredAt = DateTimeOffset.UtcNow,
                payload = new { note = "spec 020 T016" },
            });

            if ((int)response.StatusCode >= 500)
            {
                return response;
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        return response;
    }

    private async Task<long> CountAsync(string kind)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM events WHERE kind = {0}", kind)
            .SingleAsync();
    }

    private async Task RestoreStorageAsync()
    {
        DateTime now = DateTime.UtcNow;
        await ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS events_{Fab} PARTITION OF events
                FOR VALUES IN ('{Fab}') PARTITION BY RANGE (ingested_at);
            """);
        await ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS "events_{Fab}_{now:yyyyMM}" PARTITION OF events_{Fab}
                FOR VALUES FROM ('{now:yyyy-MM}-01') TO ('{now.AddMonths(1):yyyy-MM}-01');
            """);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
#pragma warning disable EF1002
        await database.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }
}
