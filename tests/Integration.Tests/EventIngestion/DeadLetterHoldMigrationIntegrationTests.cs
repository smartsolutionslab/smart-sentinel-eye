using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// T004 (spec 317, #2325) — plan.md §4. Exercises the
/// <c>AddDeadLetterHoldState</c> migration itself, against a scratch database
/// rather than the shared dev one: the shared stack's <c>dead_letters</c> and
/// <c>source_modes</c> tables are already fully migrated by the time any test
/// runs (<c>MigrationRunner</c> applies every pending migration once, at
/// stack boot — ADR-0067), so a row inserted from a test body can never be
/// "a row that existed before this migration ran". A scratch database that
/// this test migrates itself, in two steps, is the only way to put a
/// pre-migration row in front of the migration under test.
///
/// <para>
/// <b>Red on arrival, for the right reason.</b> <c>AddDeadLetterHoldState</c>
/// does not exist yet (T005), so the step-two <c>MigrateAsync()</c> below
/// applies nothing beyond what every other migration test already exercises,
/// and the final raw-SQL assertions fail because <c>dead_letters.reason</c> /
/// <c>.kind</c> / <c>.state</c> do not exist as columns at all — a genuine
/// schema gap, not a wrong call that merely fails to compile.
/// </para>
///
/// <para>
/// <b>Why a named target rather than "whatever is latest minus one".</b>
/// <c>20261006125549_AddIdempotencyRequestBinding</c> is today's latest
/// migration (<c>git log -- Infrastructure/Persistence/Migrations</c>). This
/// test migrates to exactly that name, seeds the pre-migration-shaped rows,
/// then calls the parameterless <see cref="DbContext.Database"/>
/// <c>MigrateAsync()</c> to apply whatever is pending after it — today,
/// nothing; once T005 lands, <c>AddDeadLetterHoldState</c> and nothing else.
/// The test does not need to know that migration's name in advance.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class DeadLetterHoldMigrationIntegrationTests(AspireFixture aspire)
{
    private const string LastMigrationBeforeThisSpec = "20261006125549_AddIdempotencyRequestBinding";

    [Fact]
    public async Task The_migration_backfills_reasons_by_topic_prefix_and_resets_discovery_declarations()
    {
        string scratchDatabase = $"event_ingestion_migration_scratch_{Guid.CreateVersion7():N}";
        string? baseConnectionString = await aspire.App.GetConnectionStringAsync(
            AspireFixture.EventIngestionConnectionName);
        baseConnectionString.ShouldNotBeNull(
            $"connection string '{AspireFixture.EventIngestionConnectionName}' was not provisioned by Aspire");

        NpgsqlConnectionStringBuilder scratchBuilder = new(baseConnectionString) { Database = scratchDatabase };
        string scratchConnectionString = scratchBuilder.ConnectionString;

        await CreateScratchDatabaseAsync(baseConnectionString, scratchDatabase);
        try
        {
            DbContextOptionsBuilder<EventIngestionDbContext> optionsBuilder = new();
            optionsBuilder.UseNpgsql(scratchConnectionString);
            await using EventIngestionDbContext context = new(optionsBuilder.Options);

            // Step 1: migrate only as far as today's latest migration — the
            // schema as it exists before this spec's migration is written.
            IMigrator migrator = context.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(LastMigrationBeforeThisSpec);

            // Step 2: seed rows in the pre-migration shape. Both an "event/"
            // and a "fab/" topic for dead_letters (spec.md's back-fill rule),
            // and a discovery + a strict source_modes row (FR-014).
            Guid refusedRowId = Guid.CreateVersion7();
            Guid parseFailureRowId = Guid.CreateVersion7();
            Guid discoveryRowId = Guid.CreateVersion7();
            Guid strictRowId = Guid.CreateVersion7();
            // Scratch database — no risk of colliding with another test run —
            // so plain, short, distinguishable literals rather than a Guid
            // suffix that a 16-char column truncation then collapses to a
            // shared prefix (both names start "migration-probe-").
            const string discoverySource = "probe-discovery";
            const string strictSource = "probe-strict";

            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dead_letters (dead_letter_id, topic, fab, raw_payload, error, rejected_at, version)
                VALUES ({refusedRowId}, {"event/berlin/manual/station-1"}, {"berlin"}, {"{}"}, {"EVENT_TYPE_NOT_REGISTERED: probe"}, {DateTimeOffset.UtcNow}, 0)
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dead_letters (dead_letter_id, topic, fab, raw_payload, error, rejected_at, version)
                VALUES ({parseFailureRowId}, {"fab/berlin/plc/station-2"}, {"berlin"}, {"<not-json>"}, {"envelope parse failed"}, {DateTimeOffset.UtcNow}, 0)
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO source_modes (source_mode_id, fab, source, mode, declared_at, declared_by, version)
                VALUES ({discoveryRowId}, {"migration-probe"}, {discoverySource}, {"discovery"}, {DateTimeOffset.UtcNow}, {Guid.CreateVersion7()}, 0)
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO source_modes (source_mode_id, fab, source, mode, declared_at, declared_by, version)
                VALUES ({strictRowId}, {"migration-probe"}, {strictSource}, {"strict"}, {DateTimeOffset.UtcNow}, {Guid.CreateVersion7()}, 3)
                """);

            // Step 3: apply whatever is pending beyond LastMigrationBeforeThisSpec.
            await context.Database.MigrateAsync();

            // Step 4: assert the back-fill (FR-004) and the discovery reset (FR-014).
            BackfilledDeadLetterRow refusedRow = await context.Database
                .SqlQueryRaw<BackfilledDeadLetterRow>(
                    "SELECT reason AS \"Reason\", kind AS \"Kind\", state AS \"State\" "
                    + "FROM dead_letters WHERE dead_letter_id = {0}", refusedRowId)
                .SingleAsync();
            refusedRow.Reason.ShouldBe("Refused", "an 'event/' topic is a parsed-and-refused envelope, not a parse failure");
            refusedRow.Kind.ShouldBeNull("a back-filled row has no recoverable kind (A4)");
            refusedRow.State.ShouldBe("Held");

            BackfilledDeadLetterRow parseFailureRow = await context.Database
                .SqlQueryRaw<BackfilledDeadLetterRow>(
                    "SELECT reason AS \"Reason\", kind AS \"Kind\", state AS \"State\" "
                    + "FROM dead_letters WHERE dead_letter_id = {0}", parseFailureRowId)
                .SingleAsync();
            parseFailureRow.Reason.ShouldBe("ParseFailure", "a 'fab/' topic never reached a parsed envelope");
            parseFailureRow.State.ShouldBe("Held");

            int discoveryRowCount = await context.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM source_modes WHERE source_mode_id = {0}", discoveryRowId)
                .SingleAsync();
            discoveryRowCount.ShouldBe(0, "FR-014: every discovery declaration must be deleted by the migration");

            int strictVersion = await context.Database.SqlQueryRaw<int>(
                "SELECT version AS \"Value\" FROM source_modes WHERE source_mode_id = {0}", strictRowId)
                .SingleAsync();
            strictVersion.ShouldBe(3, "a strict row must survive the migration untouched, at its prior version");
        }
        finally
        {
            await DropScratchDatabaseAsync(baseConnectionString, scratchDatabase);
        }
    }

    private static async Task CreateScratchDatabaseAsync(string baseConnectionString, string scratchDatabase)
    {
        NpgsqlConnectionStringBuilder adminBuilder = new(baseConnectionString) { Database = "postgres" };
        await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using NpgsqlCommand create = admin.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{scratchDatabase}\"";
        await create.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// <c>WITH (FORCE)</c> (PostgreSQL 13+) disconnects whatever Npgsql's
    /// connection pool is still holding open to the scratch database —
    /// disposing <see cref="EventIngestionDbContext"/> returns its connection
    /// to the pool rather than closing it, so a plain <c>DROP DATABASE</c>
    /// here raced the pool and failed with "being accessed by other users"
    /// often enough to mask the test's own assertion failures underneath it.
    /// Best-effort: a cleanup that cannot run must not fail the test that
    /// already passed or failed on its own merits.
    /// </summary>
    private static async Task DropScratchDatabaseAsync(string baseConnectionString, string scratchDatabase)
    {
        try
        {
            NpgsqlConnectionStringBuilder adminBuilder = new(baseConnectionString) { Database = "postgres" };
            await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            await using NpgsqlCommand drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{scratchDatabase}\" WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }
        catch (PostgresException)
        {
            // Best-effort cleanup only — see remarks. A leaked scratch
            // database is a cost to the shared dev Postgres, not to this
            // test's own result.
        }
    }

    private sealed record BackfilledDeadLetterRow(string Reason, string? Kind, string State);
}
