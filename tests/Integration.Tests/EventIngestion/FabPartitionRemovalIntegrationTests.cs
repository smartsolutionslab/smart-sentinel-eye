using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 019 T022 — SC-006, and the only requirement here whose failure cannot
/// be undone. Deriving storage from a list makes "absent from the list"
/// reachable for the first time; the destructive reading of that deletes a
/// plant's entire history.
///
/// <para>
/// A fab group cannot be removed from the realm at test time — the migration
/// job's credential holds <c>query-groups</c> and <c>view-users</c>, neither of
/// which can write. So removal is reproduced where it actually lands: the fab
/// simply is not in the list handed to the provisioner, which is precisely what
/// deleting its group would produce.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class FabPartitionRemovalIntegrationTests(AspireFixture aspire)
{
    [Fact]
    public async Task Provisioning_without_a_fab_leaves_its_storage_and_its_events_alone()
    {
        await using EventIngestionDbContext database = await aspire.CreateEventIngestionDbContextAsync();

        // A row that would be lost if provisioning ever dropped a partition.
        string marker = $"Removal{Guid.CreateVersion7():N}"[..20];
        await SeedDresdenEventAsync(database, marker);

        long before = await CountAsync(database, marker);
        before.ShouldBe(1, "the seed did not land, so survival proves nothing");

        // Provision for munich alone: dresden, berlin and hamburg are all
        // "removed from the realm" as far as this call can tell.
        FabPartitionProvisioner provisioner = new(NullLogger<FabPartitionProvisioner>.Instance);
        await provisioner.ProvisionAsync(
            database, [FabIdentifier.From("munich")], CancellationToken.None);

        (await PartitionExistsAsync(database, "events_dresden")).ShouldBeTrue(
            "provisioning dropped the storage of a fab that was absent from the list");
        (await CountAsync(database, marker)).ShouldBe(
            1, "provisioning destroyed events belonging to a fab absent from the list");
    }

    private static async Task SeedDresdenEventAsync(EventIngestionDbContext database, string marker)
    {
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO events (event_id, fab_id, source, device_id, kind, occurred_at, ingested_at, payload, version)
            VALUES (gen_random_uuid(), 'dresden', 'manual', 'removal-device', {0}, now(), now(), '{{}}'::jsonb, 0)
            """,
            marker);
    }

    private static async Task<long> CountAsync(EventIngestionDbContext database, string marker) =>
        await database.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM events WHERE kind = {0}", marker)
            .SingleAsync();

    private static async Task<bool> PartitionExistsAsync(EventIngestionDbContext database, string partition) =>
        await database.Database
            .SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pg_class WHERE relname = {0}", partition)
            .SingleAsync() > 0;
}
