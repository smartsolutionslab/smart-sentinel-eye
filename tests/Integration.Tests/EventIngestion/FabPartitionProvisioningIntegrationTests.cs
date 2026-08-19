using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 019 T013 — SC-001 and SC-002. A fab that exists only in the realm gets
/// its event storage without anyone writing a migration.
///
/// <para>
/// <c>berlin</c> and <c>hamburg</c> are in the realm and have never had a
/// hand-written partition. Before this feature an event filed by a berlin
/// operator returned <c>202 Accepted</c> and was then discarded inside the
/// persistence loop — recorded in T001.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class FabPartitionProvisioningIntegrationTests(AspireFixture aspire)
{
    private const string BerlinOperator = "op-berlin@berlin.test";
    private const string OperatorPassword = "Operator1234";

    [Fact]
    public async Task Every_fab_in_the_realm_has_event_storage()
    {
        IReadOnlyList<string> partitions = await FabPartitionsAsync();

        // munich and dresden were provisioned by hand; berlin was provisioned
        // from the realm by the migration job that ran before this fixture
        // handed out its first token.
        //
        // hamburg is deliberately not asserted here: it is also provisioned,
        // but FabStorageRefusalIntegrationTests drops its partition to create a
        // fab with no storage, and tests in this collection share one stack.
        partitions.ShouldContain("events_munich");
        partitions.ShouldContain("events_dresden");
        partitions.ShouldContain("events_berlin");
    }

    /// <summary>
    /// FR-004, and the half that is easy to miss: a fab partition with no month
    /// beneath it stores exactly as little as no partition at all. Provisioning
    /// must therefore run before the rollover, in the same pass.
    /// </summary>
    [Fact]
    public async Task A_newly_provisioned_fab_has_this_month_and_next()
    {
        DateTime now = DateTime.UtcNow;
        string thisMonth = now.ToString("yyyyMM", CultureInfo.InvariantCulture);
        string nextMonth = now.AddMonths(1).ToString("yyyyMM", CultureInfo.InvariantCulture);

        IReadOnlyList<string> monthly = await MonthlyPartitionsAsync("events_berlin");

        monthly.ShouldContain($"events_berlin_{thisMonth}");
        monthly.ShouldContain($"events_berlin_{nextMonth}");
    }

    /// <summary>
    /// The T001 case, repeated. Same request, same operator, same token — only
    /// the storage caught up.
    /// </summary>
    [Fact]
    public async Task An_event_filed_by_an_operator_of_a_realm_only_fab_is_stored()
    {
        string kind = $"Provisioned{Guid.CreateVersion7():N}"[..24];

        using HttpClient berlin = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", BerlinOperator, OperatorPassword);

        HttpResponseMessage filed = await berlin.PostAsJsonAsync("/events/manual", new
        {
            deviceId = "provisioning-device",
            kind,
            occurredAt = DateTimeOffset.UtcNow,
            payload = new { note = "spec 019 US1" },
        });
        filed.StatusCode.ShouldBe(HttpStatusCode.Created, await filed.Content.ReadAsStringAsync());

        int found = 0;
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline && found == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            HttpResponseMessage listed = await berlin.GetAsync($"/events?kind={kind}");
            if (listed.IsSuccessStatusCode)
            {
                JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();
                found = page.GetProperty("items").GetArrayLength();
            }
        }

        found.ShouldBe(1, "the event was accepted but never stored — the T001 defect");
    }

    private async Task<IReadOnlyList<string>> FabPartitionsAsync()
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<string>("""
                SELECT child.relname AS "Value"
                FROM   pg_inherits
                JOIN   pg_class parent ON pg_inherits.inhparent = parent.oid
                JOIN   pg_class child  ON pg_inherits.inhrelid  = child.oid
                WHERE  parent.relname = 'events'
                """)
            .ToListAsync();
    }

    private async Task<IReadOnlyList<string>> MonthlyPartitionsAsync(string fabPartition)
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();
        return await database.Database
            .SqlQueryRaw<string>("""
                SELECT child.relname AS "Value"
                FROM   pg_inherits
                JOIN   pg_class parent ON pg_inherits.inhparent = parent.oid
                JOIN   pg_class child  ON pg_inherits.inhrelid  = child.oid
                WHERE  parent.relname = {0}
                """, fabPartition)
            .ToListAsync();
    }
}
