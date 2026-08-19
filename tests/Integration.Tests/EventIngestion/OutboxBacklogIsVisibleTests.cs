using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 021 T023, FR-008 and FR-010. This feature replaced a silent loss with a
/// durable retry, which is only an improvement if the retry is visible: an
/// outbox quietly growing looks exactly like an empty one until the disk fills.
///
/// <para>
/// It also pins down the tables the health check reads. Their names are
/// Wolverine's, not ours, and a health check querying a table that does not
/// exist reports nothing wrong — the query fails, the failure is swallowed as
/// "the database check owns this", and the backlog stays invisible for the same
/// reason it was invisible before. So the names are asserted rather than
/// assumed.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class OutboxBacklogIsVisibleTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string OutboxSchema = "wolverine_event_ingestion";

    [Fact]
    public async Task The_tables_the_backlog_is_read_from_exist()
    {
        string[] tables = await TablesAsync();
        output.WriteLine($"{OutboxSchema}: {string.Join(", ", tables)}");

        tables.ShouldContain(
            "wolverine_outgoing_envelopes",
            "the health check and every backlog assertion read this table by name; "
            + "if it is not here they are reporting on nothing");

        tables.ShouldContain(
            "wolverine_dead_letters",
            "FR-010 requires a message that can never be delivered to be recorded "
            + "durably and countably — this is where Wolverine records it");
    }

    /// <summary>
    /// FR-008. What an operator needs before a backlog becomes a disk problem:
    /// how many are waiting, and whether delivery is stuck.
    ///
    /// <para>
    /// <b>FR-008 asked for the age of the oldest, and that is not obtainable.</b>
    /// The first version of this test guessed a timestamp column and failed with
    /// 42703 — and the health check had guessed the same name, where the error
    /// was swallowed and reported as Healthy. Asking the catalogue shows why:
    /// the table is id, owner_id, destination, deliver_by, body, attempts,
    /// message_type, and records no enqueue time. Attempts answer what the age
    /// was a proxy for, so that is what is reported and the substitution is on
    /// the record rather than quietly made.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_backlog_can_be_counted_and_aged()
    {
        string[] columns = await ColumnsAsync("wolverine_outgoing_envelopes");
        output.WriteLine($"outgoing columns: {string.Join(", ", columns)}");

        columns.ShouldContain(
            OutboxBacklogHealthCheck<EventIngestionDbContext>.AttemptsColumn,
            "the health check reads this column to report whether delivery is stuck; "
            + "if it is not here the check reports on nothing");

        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        long pending = await database.Database
            .SqlQueryRaw<long>(
                $"SELECT count(*) AS \"Value\" FROM {OutboxSchema}.wolverine_outgoing_envelopes")
            .SingleAsync();

        output.WriteLine($"pending={pending}");

        // No assertion on the count: a healthy system is usually at zero and a
        // busy one is briefly not, so asserting either would make this a timing
        // race. What is asserted is that the question is answerable — which is
        // the whole of FR-008.
        pending.ShouldBeGreaterThanOrEqualTo(0);
    }

    private async Task<string[]> ColumnsAsync(string table)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<string>(
                "SELECT column_name AS \"Value\" FROM information_schema.columns "
                + "WHERE table_schema = {0} AND table_name = {1}",
                OutboxSchema,
                table)
            .ToArrayAsync();
    }

    private async Task<string[]> TablesAsync()
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<string>(
                "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = {0}",
                OutboxSchema)
            .ToArrayAsync();
    }
}
