using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 021 T007 and T008 — the claims a unit test cannot make, because they
/// are about a real transaction and a real outbox rather than about this
/// repository's control flow.
///
/// <para>
/// The unit cases prove the repository captures before it commits. What is left
/// is whether the database agrees: that the message rows and the event rows are
/// one transaction, so a write that rolls back takes its announcement with it.
/// A fake asserting that would be testing the fake.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class OutboxSharesTheWritesFateTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string Fab = "hamburg";
    private const string Operator = "op-hamburg@hamburg.test";
    private const string OperatorPassword = "Operator1234";
    private const string OutboxSchema = "wolverine_event_ingestion";

    /// <summary>
    /// FR-001, SC-003 — and the failure a naive fix introduces. Capturing the
    /// announcement early is only safe if it is discarded when the write is not
    /// committed. Announcing a write that never happened is worse than losing
    /// the announcement of one that did: consumers act on it, and there is no
    /// row to reconcile against afterwards.
    /// </summary>
    [Fact]
    public async Task A_write_that_cannot_commit_leaves_no_message_behind()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", Operator, OperatorPassword);

        string kind = $"Rollback{Guid.CreateVersion7():N}"[..20];
        long before = await PendingMessagesAsync();
        output.WriteLine($"pending messages before: {before}");

        await ExecuteAsync($"DROP TABLE IF EXISTS events_{Fab};");
        output.WriteLine($"dropped events_{Fab} — the write can no longer commit");

        try
        {
            HttpResponseMessage refused = await PostUntilRefusedAsync(client, kind);
            output.WriteLine($"POST /events/manual -> {(int)refused.StatusCode}");
            ((int)refused.StatusCode).ShouldBeGreaterThanOrEqualTo(500);

            (await StoredAsync(kind)).ShouldBe(0, "a refused write stored the event anyway");

            // The assertion. The announcement was captured before the commit was
            // attempted, so if the rollback does not take it with it, it is
            // sitting in the outbox waiting to tell eight other contexts about
            // an event that does not exist.
            long after = await PendingMessagesAsync();
            output.WriteLine($"pending messages after: {after}");
            after.ShouldBe(
                before, "a rolled-back write left an announcement behind for an event that does not exist");
        }
        finally
        {
            await RestoreStorageAsync();
        }
    }

    /// <summary>
    /// FR-002. The ordinary path, asserted from the other side: a write that
    /// commits leaves nothing owed. If the outbox retained rows after a healthy
    /// write, the backlog FR-008 asks us to watch would grow for no reason and
    /// the signal would be worthless.
    /// </summary>
    [Fact]
    public async Task A_write_that_commits_leaves_nothing_owed()
    {
        using HttpClient client = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", Operator, OperatorPassword);

        string kind = $"Committed{Guid.CreateVersion7():N}"[..20];
        HttpResponseMessage created = await client.PostAsJsonAsync("/events/manual", new
        {
            deviceId = "outbox-device",
            kind,
            occurredAt = DateTimeOffset.UtcNow,
            payload = new { note = "spec 021 T007" },
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await StoredAsync(kind)).ShouldBe(1);

        long pending = await DrainedAsync(TimeSpan.FromSeconds(30));
        output.WriteLine($"pending messages after a healthy write: {pending}");
        pending.ShouldBe(0, "the outbox is retaining messages it has already delivered");
    }

    private async Task<long> DrainedAsync(TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        long pending = await PendingMessagesAsync();

        while (pending > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            pending = await PendingMessagesAsync();
        }

        return pending;
    }

    /// <summary>
    /// Reads Wolverine's own table. Nothing in this repository writes it — which
    /// is the point of R1's decision not to hand-roll an outbox — so this is the
    /// only way to see a pending announcement at all.
    /// </summary>
    private async Task<long> PendingMessagesAsync()
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>(
                $"SELECT count(*) AS \"Value\" FROM {OutboxSchema}.wolverine_outgoing_envelopes")
            .SingleAsync();
    }

    private async Task<long> StoredAsync(string kind)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM events WHERE kind = {0}", kind)
            .SingleAsync();
    }

    private static async Task<HttpResponseMessage> PostUntilRefusedAsync(HttpClient client, string kind)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(90);
        HttpResponseMessage response = null!;

        while (DateTimeOffset.UtcNow < deadline)
        {
            response = await client.PostAsJsonAsync("/events/manual", new
            {
                deviceId = "outbox-device",
                kind,
                occurredAt = DateTimeOffset.UtcNow,
                payload = new { note = "spec 021 T008" },
            });

            if ((int)response.StatusCode >= 500)
            {
                return response;
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        return response;
    }

    /// <summary>
    /// Recreates the partition and <b>both</b> monthly children, because the drop
    /// took whatever was there. Restoring only the current month leaves a run
    /// started near a month boundary — or any later test relying on next month's
    /// child, which the rollover migrator creates — looking at a fixture state
    /// nobody set up, and the failure surfaces a long way from here.
    /// </summary>
    private async Task RestoreStorageAsync()
    {
        DateTime now = DateTime.UtcNow;
        DateTime next = now.AddMonths(1);

        await ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS events_{Fab} PARTITION OF events
                FOR VALUES IN ('{Fab}') PARTITION BY RANGE (ingested_at);
            """);
        await ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS "events_{Fab}_{now:yyyyMM}" PARTITION OF events_{Fab}
                FOR VALUES FROM ('{now:yyyy-MM}-01') TO ('{next:yyyy-MM}-01');
            """);
        await ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS "events_{Fab}_{next:yyyyMM}" PARTITION OF events_{Fab}
                FOR VALUES FROM ('{next:yyyy-MM}-01') TO ('{next.AddMonths(1):yyyy-MM}-01');
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
