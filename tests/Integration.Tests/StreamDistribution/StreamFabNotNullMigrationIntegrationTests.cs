using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 423 (#1467), R4. Exercises the <c>StreamFabNotNull</c> migration
/// against a scratch database rather than the shared dev one: the shared
/// stack's <c>streams</c> table is already fully migrated by the time any test
/// runs (<c>MigrationRunner</c> applies every migration once, at stack boot —
/// ADR-0067), so a null-fab row can only be put in front of the migration by a
/// scratch database this test migrates itself, in two steps (the pattern
/// <c>DeadLetterHoldMigrationIntegrationTests</c> established).
///
/// <para>
/// <b>Red on arrival, for the right reason.</b> The migration does not exist
/// yet, so step two applies nothing: no exception is raised for a null-fab
/// stream, the column stays nullable, and a later <c>fab = NULL</c> write
/// succeeds.
/// </para>
///
/// <para>
/// <b>The target is a named predecessor</b> (the newest migration before
/// <c>StreamFabNotNull</c>), not "latest minus one": the test migrates to it,
/// seeds, then calls the parameterless <c>MigrateAsync()</c> to apply whatever
/// follows — today nothing, afterwards the guarded <c>StreamFabNotNull</c>.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class StreamFabNotNullMigrationIntegrationTests(AspireFixture aspire)
{
    private const string LastMigrationBeforeThisSpec = "20261018083817_RecordStreamSourceCameraVersion";

    [Fact]
    public async Task The_migration_refuses_while_a_stream_has_no_fab_and_leaves_the_column_nullable()
    {
        await WithScratchDatabaseAsync(async scratchConnectionString =>
        {
            var streamIdentifier = Guid.CreateVersion7();
            var cameraIdentifier = Guid.CreateVersion7();
            await MigrateToPredecessorAsync(scratchConnectionString);
            await InsertStreamAsync(scratchConnectionString, streamIdentifier, cameraIdentifier, fab: null);

            var refusal = await Should.ThrowAsync<Exception>(() => MigrateToLatestAsync(scratchConnectionString));

            var postgres = PostgresExceptionIn(refusal);
            postgres.SqlState.ShouldBe("P0001", $"the guard raises a user exception; got: {refusal}");
            postgres.MessageText.ShouldContain(streamIdentifier.ToString());
            postgres.MessageText.ShouldContain(cameraIdentifier.ToString());
            (await FabIsNullableAsync(scratchConnectionString)).ShouldBe(
                "YES", "a refused migration must leave the column as it found it");
            (await AppliedStreamFabMigrationsAsync(scratchConnectionString)).ShouldBe(
                0, "a refused migration must not be recorded as applied, so the next start retries it");
        });
    }

    [Fact]
    public async Task The_migration_applies_once_the_missing_fab_is_set()
    {
        await WithScratchDatabaseAsync(async scratchConnectionString =>
        {
            var streamIdentifier = Guid.CreateVersion7();
            await MigrateToPredecessorAsync(scratchConnectionString);
            await InsertStreamAsync(scratchConnectionString, streamIdentifier, Guid.CreateVersion7(), fab: null);
            await Should.ThrowAsync<Exception>(() => MigrateToLatestAsync(scratchConnectionString));

            await ExecuteAsync(
                scratchConnectionString,
                "UPDATE streams SET fab = 'munich' WHERE stream_id = @stream",
                ("stream", streamIdentifier));
            await MigrateToLatestAsync(scratchConnectionString);

            (await FabIsNullableAsync(scratchConnectionString)).ShouldBe("NO");
        });
    }

    [Fact]
    public async Task The_migration_applies_cleanly_when_there_are_no_streams()
    {
        await WithScratchDatabaseAsync(async scratchConnectionString =>
        {
            await MigrateToPredecessorAsync(scratchConnectionString);

            await MigrateToLatestAsync(scratchConnectionString);

            (await FabIsNullableAsync(scratchConnectionString)).ShouldBe("NO");
        });
    }

    [Fact]
    public async Task After_the_migration_a_stream_cannot_be_written_without_a_fab()
    {
        await WithScratchDatabaseAsync(async scratchConnectionString =>
        {
            var streamIdentifier = Guid.CreateVersion7();
            await MigrateToPredecessorAsync(scratchConnectionString);
            await InsertStreamAsync(scratchConnectionString, streamIdentifier, Guid.CreateVersion7(), fab: "munich");
            await MigrateToLatestAsync(scratchConnectionString);

            var refusal = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(
                scratchConnectionString,
                "UPDATE streams SET fab = NULL WHERE stream_id = @stream",
                ("stream", streamIdentifier)));

            refusal.SqlState.ShouldBe("23502");
        });
    }

    private static StreamDistributionDbContext NewContext(string connectionString)
    {
        DbContextOptionsBuilder<StreamDistributionDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        return new StreamDistributionDbContext(optionsBuilder.Options);
    }

    private static async Task MigrateToPredecessorAsync(string connectionString)
    {
        await using var context = NewContext(connectionString);
        var migrator = context.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync(LastMigrationBeforeThisSpec);
    }

    private static async Task MigrateToLatestAsync(string connectionString)
    {
        await using var context = NewContext(connectionString);
        await context.Database.MigrateAsync();
    }

    private static async Task InsertStreamAsync(
        string connectionString, Guid streamIdentifier, Guid cameraIdentifier, string? fab)
    {
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO streams (
                stream_id, camera_id, fab, mediamtx_path, state, transcode_mode,
                source_url, provisioned_at, provisioned_by, version)
            VALUES (
                @stream, @camera, @fab, @path, 'Healthy', 'Unknown',
                'rtsp://probe/stream', now(), @operator, 0)
            """,
            ("stream", streamIdentifier),
            ("camera", cameraIdentifier),
            ("fab", (object?)fab ?? DBNull.Value),
            ("path", $"cam-{cameraIdentifier:N}"),
            ("operator", Guid.CreateVersion7()));
    }

    private static async Task ExecuteAsync(
        string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((var name, var value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> FabIsNullableAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT is_nullable FROM information_schema.columns "
            + "WHERE table_name = 'streams' AND column_name = 'fab'";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> AppliedStreamFabMigrationsAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*)::int FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%StreamFabNotNull'";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static PostgresException PostgresExceptionIn(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"expected a PostgresException from the migration guard, but got: {exception}");
    }

    private async Task WithScratchDatabaseAsync(Func<string, Task> body)
    {
        var scratchDatabase = $"stream_distribution_migration_scratch_{Guid.CreateVersion7():N}";
        var baseConnectionString = await aspire.App.GetConnectionStringAsync(
            AspireFixture.StreamDistributionConnectionName);
        baseConnectionString.ShouldNotBeNull(
            $"connection string '{AspireFixture.StreamDistributionConnectionName}' was not provisioned by Aspire");

        NpgsqlConnectionStringBuilder scratchBuilder = new(baseConnectionString) { Database = scratchDatabase };

        await AdministerAsync(baseConnectionString, $"CREATE DATABASE \"{scratchDatabase}\"");
        try
        {
            await body(scratchBuilder.ConnectionString);
        }
        finally
        {
            try
            {
                await AdministerAsync(
                    baseConnectionString, $"DROP DATABASE IF EXISTS \"{scratchDatabase}\" WITH (FORCE)");
            }
            catch (PostgresException)
            {
                // Best-effort cleanup only: a leaked scratch database costs the shared
                // dev Postgres, not this test's own result.
            }
        }
    }

    private static async Task AdministerAsync(string baseConnectionString, string statement)
    {
        NpgsqlConnectionStringBuilder adminBuilder = new(baseConnectionString) { Database = "postgres" };
        await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = statement;
        await command.ExecuteNonQueryAsync();
    }
}
