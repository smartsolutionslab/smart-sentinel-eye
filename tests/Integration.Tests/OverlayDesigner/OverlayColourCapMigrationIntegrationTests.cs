using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.Integration.Tests.OverlayDesigner;

/// <summary>
/// Spec 463 (#3108), ADR-0190, plan C5: the colour-cap migration against a scratch database (spec 317's
/// pattern, see <c>DeadLetterHoldMigrationIntegrationTests</c>). Four stored element colours across draft,
/// published and archived revisions are put in front of the <c>AddOverlayColourMigrationRecord</c> EF
/// migration and the <c>OverlayColourCapMigrator</c>; the three over the cap are darkened (a Text colour that
/// the darkening leaves illegible has its alpha raised), the fourth is untouched, and one record row per
/// change names both colours. A second run changes nothing.
///
/// <para>
/// <b>Red on arrival.</b> Neither the migration nor the migrator exists yet. The migrator is reached by
/// type name so this file compiles without it; the failing assertion is that the type is missing, and once
/// it exists, that <c>overlay_colour_migrations</c> and the rewritten colours are absent.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayColourCapMigrationIntegrationTests(AspireFixture aspire)
{
    private const string LastMigrationBeforeThisSpec = "20261025071805_AddIdempotencyResourceVersion";

    private const string MigratorTypeName =
        "SmartSentinelEye.OverlayDesigner.Infrastructure.Persistence.OverlayColourCapMigrator";

    [Fact]
    public async Task The_migration_darkens_over_the_cap_colours_records_each_change_and_is_idempotent()
    {
        var scratchDatabase = $"overlay_designer_colour_cap_scratch_{Guid.CreateVersion7():N}";
        var baseConnectionString = await aspire.App.GetConnectionStringAsync(
            AspireFixture.OverlayDesignerConnectionName);
        baseConnectionString.ShouldNotBeNull(
            $"connection string '{AspireFixture.OverlayDesignerConnectionName}' was not provisioned by Aspire");

        NpgsqlConnectionStringBuilder scratchBuilder = new(baseConnectionString) { Database = scratchDatabase };
        var scratchConnectionString = scratchBuilder.ConnectionString;

        await CreateScratchDatabaseAsync(baseConnectionString, scratchDatabase);
        try
        {
            DbContextOptionsBuilder<OverlayDesignerDbContext> optionsBuilder = new();
            optionsBuilder.UseNpgsql(scratchConnectionString);
            await using OverlayDesignerDbContext context = new(optionsBuilder.Options);

            var migrator = context.GetInfrastructure().GetRequiredService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync(LastMigrationBeforeThisSpec);

            var draftRevision = await SeedRevisionAsync(context, "Draft", "draft-wall");
            var publishedRevision = await SeedRevisionAsync(context, "Published", "published-wall");
            var archivedRevision = await SeedRevisionAsync(context, "Archived", "archived-wall");

            await SeedElementAsync(context, draftRevision, 0, "Text", "#FFFF0099");
            await SeedElementAsync(context, draftRevision, 1, "Text", "#D32F2FFF");
            await SeedElementAsync(context, publishedRevision, 0, "Text", "#FFFFFFD9");
            await SeedElementAsync(context, archivedRevision, 0, "Box", "#FFEB3BFF");

            await context.Database.MigrateAsync();
            await RunColourCapMigratorAsync(scratchConnectionString);

            (await ColourOf(context, draftRevision, 0)).ShouldBe("#A5A500BA");
            (await ColourOf(context, draftRevision, 1)).ShouldBe("#D32F2FFF");
            (await ColourOf(context, publishedRevision, 0)).ShouldBe("#9F9F9FD9");
            (await ColourOf(context, archivedRevision, 0)).ShouldBe("#AFA125FF");
            (await StateOf(context, publishedRevision)).ShouldBe("Published");

            var records = await RecordsAsync(context);
            records.Count.ShouldBe(3);
            records.ShouldContain(new MigrationRecord(draftRevision, 0, "#FFFF0099", "#A5A500BA"));
            records.ShouldContain(new MigrationRecord(publishedRevision, 0, "#FFFFFFD9", "#9F9F9FD9"));
            records.ShouldContain(new MigrationRecord(archivedRevision, 0, "#FFEB3BFF", "#AFA125FF"));

            await RunColourCapMigratorAsync(scratchConnectionString);

            (await RecordsAsync(context)).Count.ShouldBe(3, "a second run must add no record row");
            (await ColourOf(context, publishedRevision, 0)).ShouldBe("#9F9F9FD9");
        }
        finally
        {
            await DropScratchDatabaseAsync(baseConnectionString, scratchDatabase);
        }
    }

    private static async Task RunColourCapMigratorAsync(string scratchConnectionString)
    {
        var infrastructure = typeof(OverlayDesignerMigrator).Assembly;
        var migratorType = infrastructure.GetType(MigratorTypeName);
        migratorType.ShouldNotBeNull($"{MigratorTypeName} must exist (plan C5)");

        var loggerType = typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>).MakeGenericType(migratorType);
        var logger = loggerType.GetField("Instance")!.GetValue(null);
        ScratchContextFactory factory = new(scratchConnectionString);

        var constructor = migratorType.GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType.IsAssignableFrom(typeof(ScratchContextFactory))
                ? (object)factory
                : logger!)
            .ToArray();
        var migrator = (SmartSentinelEye.ServiceDefaults.IMigrator)constructor.Invoke(arguments);

        await migrator.RunAsync(CancellationToken.None);
    }

    private static async Task<Guid> SeedRevisionAsync(OverlayDesignerDbContext context, string state, string name)
    {
        var overlayId = Guid.CreateVersion7();
        var revisionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var by = Guid.CreateVersion7();
        DateTimeOffset? published = state == "Draft" ? null : now;
        DateTimeOffset? archived = state == "Archived" ? now : null;

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO overlays (overlay_id, name, created_at, created_by, version, archived_at)
            VALUES ({overlayId}, {name}, {now}, {by}, 0, {archived})
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO overlay_revisions (revision_id, overlay_id, revision_number, state, created_at, created_by, published_at, archived_at)
            VALUES ({revisionId}, {overlayId}, 1, {state}, {now}, {by}, {published}, {archived})
            """);

        return revisionId;
    }

    private static async Task SeedElementAsync(
        OverlayDesignerDbContext context,
        Guid revisionId,
        int ordinal,
        string kind,
        string color)
    {
        var text = kind == "Text" ? "reading" : null;
        int? fontSize = kind == "Text" ? 14 : null;

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO overlay_revision_elements (revision_id, ordinal, kind, color, x, y, width, height, text, font_size_px)
            VALUES ({revisionId}, {ordinal}, {kind}, {color}, 0.1, 0.1, 0.2, 0.2, {text}, {fontSize})
            """);
    }

    private static Task<string> ColourOf(OverlayDesignerDbContext context, Guid revisionId, int ordinal) =>
        context.Database.SqlQueryRaw<string>(
            "SELECT color AS \"Value\" FROM overlay_revision_elements WHERE revision_id = {0} AND ordinal = {1}",
            revisionId,
            ordinal).SingleAsync();

    private static Task<string> StateOf(OverlayDesignerDbContext context, Guid revisionId) =>
        context.Database.SqlQueryRaw<string>(
            "SELECT state AS \"Value\" FROM overlay_revisions WHERE revision_id = {0}",
            revisionId).SingleAsync();

    private static Task<List<MigrationRecord>> RecordsAsync(OverlayDesignerDbContext context) =>
        context.Database.SqlQueryRaw<MigrationRecord>(
            "SELECT revision_id AS \"RevisionId\", ordinal AS \"Ordinal\", previous_color AS \"PreviousColor\", migrated_color AS \"MigratedColor\" "
            + "FROM overlay_colour_migrations").ToListAsync();

    private static async Task CreateScratchDatabaseAsync(string baseConnectionString, string scratchDatabase)
    {
        NpgsqlConnectionStringBuilder adminBuilder = new(baseConnectionString) { Database = "postgres" };
        await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using var create = admin.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{scratchDatabase}\"";
        await create.ExecuteNonQueryAsync();
    }

    /// <summary>Best-effort cleanup; <c>WITH (FORCE)</c> disconnects pooled connections (see spec 317's test).</summary>
    private static async Task DropScratchDatabaseAsync(string baseConnectionString, string scratchDatabase)
    {
        try
        {
            NpgsqlConnectionStringBuilder adminBuilder = new(baseConnectionString) { Database = "postgres" };
            await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{scratchDatabase}\" WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }
        catch (PostgresException)
        {
            // A leaked scratch database must not fail a test that already passed or failed on its merits.
        }
    }

    private sealed record MigrationRecord(Guid RevisionId, int Ordinal, string PreviousColor, string MigratedColor);

    private sealed class ScratchContextFactory(string connectionString) : IDbContextFactory<OverlayDesignerDbContext>
    {
        public OverlayDesignerDbContext CreateDbContext()
        {
            DbContextOptionsBuilder<OverlayDesignerDbContext> builder = new();
            builder.UseNpgsql(connectionString);
            return new OverlayDesignerDbContext(builder.Options);
        }
    }
}
