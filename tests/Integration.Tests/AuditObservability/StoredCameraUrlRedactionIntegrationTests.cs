using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.AuditObservability.Infrastructure.Redaction;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Spec 449 (#2990), R1-R5 and C1. <c>AuditUrlRedactionStep</c> rewrites the camera URLs that audit rows stored
/// before spec 377 reduced them at capture (ADR-0184). The step is driven in-process against the real TimescaleDB
/// of the Aspire fixture, as <see cref="DropChunkIdentityIntegrationTests"/> drives the chunk inventory: the
/// MigrationRunner of the fixture already ran it as a dry run at boot. Rows are seeded by direct INSERT with
/// synthetic markers and every assertion re-reads the row from the database, so none checks its own input.
///
/// <para>
/// The database is shared across the collection, so each test asserts on the <c>audit_id</c>s it seeded;
/// the one test that counts rows (R3) applies once first to clear any residue.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class StoredCameraUrlRedactionIntegrationTests(AspireFixture aspire)
{
    private const string UnrelatedKind = "UrlRedactionUnrelatedSeedV1";

    private static readonly DateTimeOffset Recent = DateTimeOffset.UtcNow.AddDays(-1);

    private sealed record Seed(Guid AuditIdentifier, string Kind, DateTimeOffset OccurredAt, string Payload, int Size, int SchemaVersion);

    private sealed record StoredRow(string Payload, int Size, int Version);

    private static string Marker() => "SECRET" + Guid.NewGuid().ToString("N");

    private static EventMetadata Metadata(DateTimeOffset at) => new(Guid.CreateVersion7(), at, Fab: "munich", Actor: null);

    // The shape written before spec 377: the full contract, serialised as is, with no DetailChanged member.
    private static Seed UnredactedRegistered(string url, DateTimeOffset at, int schemaVersion = 1)
    {
        var json = JsonSerializer.Serialize(
            new CameraRegisteredV1(Guid.CreateVersion7(), "north-gate", url, at, Guid.CreateVersion7(), Metadata(at)));

        return new(Guid.CreateVersion7(), nameof(CameraRegisteredV1), at, json, Encoding.UTF8.GetByteCount(json), schemaVersion);
    }

    private static Seed UnredactedChanged(string previousUrl, string url, DateTimeOffset at)
    {
        var json = JsonSerializer.Serialize(
            new CameraAddressChangedV1(
                Guid.CreateVersion7(), "munich", previousUrl, url, at, Guid.CreateVersion7(), Metadata(at)));

        return new(Guid.CreateVersion7(), nameof(CameraAddressChangedV1), at, json, Encoding.UTF8.GetByteCount(json), 1);
    }

    // The shape the capture handler writes today.
    private static Seed RedactedAtCapture(string previousUrl, string url, DateTimeOffset at)
    {
        var json = AuditPayloadRedaction.Serialize(
            new CameraAddressChangedV1(
                Guid.CreateVersion7(), "munich", previousUrl, url, at, Guid.CreateVersion7(), Metadata(at)));

        return new(Guid.CreateVersion7(), nameof(CameraAddressChangedV1), at, json, Encoding.UTF8.GetByteCount(json), 1);
    }

    private static Seed Unrelated(DateTimeOffset at)
    {
        var json = $$"""{"Note":"{{Marker()}}","Url":"rtsp://unrelated.test/path?token=kept"}""";

        return new(Guid.CreateVersion7(), UnrelatedKind, at, json, Encoding.UTF8.GetByteCount(json), 1);
    }

    private static int Bytes(string json) => Encoding.UTF8.GetByteCount(json);

    [Fact]
    public async Task An_applied_run_rewrites_the_urls_the_size_and_the_version_of_each_unredacted_row()
    {
        var secret = Marker();
        var registered = UnredactedRegistered($"rtsp://user:{secret}@cam-a.test:8554/live/{secret}?token={secret}&x=1", Recent);
        var changed = UnredactedChanged($"rtsp://cam-b.test/old/{secret}", $"rtsp://{secret}@cam-c.test/new?k={secret}", Recent);
        await SeedAsync(registered, changed);

        await RunAsync(apply: true);

        var registeredRow = await ReadAsync(registered.AuditIdentifier);
        var registeredJson = JsonDocument.Parse(registeredRow.Payload).RootElement;
        registeredJson.GetProperty("Url").GetString().ShouldBe("rtsp://cam-a.test:8554");
        registeredJson.GetProperty("Name").GetString().ShouldBe("north-gate", "no other member changes");
        registeredRow.Payload.ShouldNotContain(secret);
        registeredRow.Version.ShouldBe(2);
        registeredRow.Size.ShouldBe(
            Bytes(JsonSerializer.Serialize(JsonSerializer.Deserialize<CameraRegisteredV1>(registered.Payload)! with { Url = "rtsp://cam-a.test:8554" })),
            "payload_size_bytes stays on the serialiser's basis, escaping included");

        var changedRow = await ReadAsync(changed.AuditIdentifier);
        var changedJson = JsonDocument.Parse(changedRow.Payload).RootElement;
        changedJson.GetProperty("PreviousUrl").GetString().ShouldBe("rtsp://cam-b.test");
        changedJson.GetProperty("Url").GetString().ShouldBe("rtsp://cam-c.test");
        changedJson.TryGetProperty("DetailChanged", out _).ShouldBeFalse("the rewrite adds no member");
        changedRow.Payload.ShouldNotContain(secret);
        changedRow.Version.ShouldBe(2);
        var changedOrigins = JsonSerializer.Deserialize<CameraAddressChangedV1>(changed.Payload)! with
        {
            PreviousUrl = "rtsp://cam-b.test",
            Url = "rtsp://cam-c.test",
        };
        changedRow.Size.ShouldBe(
            Bytes(JsonSerializer.Serialize(changedOrigins)),
            "payload_size_bytes stays on the serialiser's basis for the address change too");
    }

    [Fact]
    public async Task A_dry_run_changes_nothing_and_leaves_the_rows_for_the_applied_run()
    {
        var secret = Marker();
        var seeded = UnredactedRegistered($"rtsp://cam-a.test/{secret}?token={secret}", Recent);
        await SeedAsync(seeded);
        var before = await ReadAsync(seeded.AuditIdentifier);

        var dry = await RunAsync(apply: false);

        (await ReadAsync(seeded.AuditIdentifier)).ShouldBe(before, "a dry run issues no DML");
        dry.Entries.ShouldNotBeEmpty();
        dry.Entries.ShouldAllBe(entry => !entry.Message.Contains(secret, StringComparison.Ordinal));

        await RunAsync(apply: true);

        var after = await ReadAsync(seeded.AuditIdentifier);
        after.Version.ShouldBe(2, "the dry run consumed nothing");
        after.Payload.ShouldNotContain(secret);
    }

    [Fact]
    public async Task The_applied_run_reports_how_many_unredacted_rows_it_found_rewrote_and_left()
    {
        await RunAsync(apply: true);
        var secret = Marker();
        var unredacted = UnredactedRegistered($"rtsp://cam-a.test/{secret}", Recent);
        var postCapture = RedactedAtCapture($"rtsp://cam-b.test/old/{secret}", $"rtsp://cam-b.test/new/{secret}", Recent);
        var alreadyRewritten = UnredactedRegistered($"rtsp://cam-d.test/{secret}", Recent, schemaVersion: 2);
        await SeedAsync(unredacted, postCapture, alreadyRewritten);

        var applied = await RunAsync(apply: true);

        var report = applied.Entries.Where(entry => entry.State.ContainsKey("Rewritten")).ShouldHaveSingleItem();
        Convert.ToInt32(report.State["CountBefore"], CultureInfo.InvariantCulture).ShouldBe(1);
        Convert.ToInt32(report.State["Rewritten"], CultureInfo.InvariantCulture).ShouldBe(1);
        Convert.ToInt32(report.State["CountAfter"], CultureInfo.InvariantCulture).ShouldBe(0);
        (await ReadAsync(alreadyRewritten.AuditIdentifier)).Payload.ShouldContain(secret, Case.Sensitive, "a row at version 2 is not read");
    }

    // The compression below acts on a whole chunk, not on the seeded row alone: every row sharing the chunk
    // is compressed with it, so the rewrite is proved against compressed storage generally.
    [Fact]
    public async Task A_row_in_a_compressed_chunk_is_rewritten_too()
    {
        var secret = Marker();
        var occurredAt = DateTimeOffset.UtcNow.AddDays(-45);
        var seeded = UnredactedRegistered($"rtsp://cam-a.test/{secret}?token={secret}", occurredAt);
        await SeedAsync(seeded);
        await CompressChunkContainingAsync(occurredAt);
        (await ChunkIsCompressedAsync(occurredAt)).ShouldBeTrue("premise: the seeded row must sit in a compressed chunk");

        await RunAsync(apply: true);

        var row = await ReadAsync(seeded.AuditIdentifier);
        row.Version.ShouldBe(2);
        row.Payload.ShouldNotContain(secret);
        JsonDocument.Parse(row.Payload).RootElement.GetProperty("Url").GetString().ShouldBe("rtsp://cam-a.test");
    }

    [Fact]
    public async Task Running_again_rewrites_nothing_and_a_later_row_is_rewritten_alone()
    {
        var secret = Marker();
        var first = UnredactedRegistered($"rtsp://cam-a.test/{secret}", Recent);
        await SeedAsync(first);
        await RunAsync(apply: true);
        var afterFirst = await ReadAsync(first.AuditIdentifier);

        var second = await RunAsync(apply: true);

        var report = second.Entries.Where(entry => entry.State.ContainsKey("Rewritten")).ShouldHaveSingleItem();
        Convert.ToInt32(report.State["Rewritten"], CultureInfo.InvariantCulture).ShouldBe(0);
        (await ReadAsync(first.AuditIdentifier)).ShouldBe(afterFirst, "a second run does not touch a rewritten row");

        var later = UnredactedRegistered($"rtsp://cam-e.test/{secret}", Recent);
        await SeedAsync(later);
        var third = await RunAsync(apply: true);

        Convert.ToInt32(
            third.Entries.Single(entry => entry.State.ContainsKey("Rewritten")).State["Rewritten"],
            CultureInfo.InvariantCulture).ShouldBe(1);
        (await ReadAsync(later.AuditIdentifier)).Version.ShouldBe(2);
        (await ReadAsync(first.AuditIdentifier)).ShouldBe(afterFirst);
    }

    [Fact]
    public async Task A_row_redacted_at_capture_and_a_row_of_another_kind_stay_byte_identical()
    {
        var secret = Marker();
        var postCapture = RedactedAtCapture($"rtsp://cam-b.test/old/{secret}", $"rtsp://cam-b.test:8554/new/{secret}", Recent);
        var unrelated = Unrelated(Recent);
        var unredacted = UnredactedRegistered($"rtsp://cam-a.test/{secret}", Recent);
        await SeedAsync(postCapture, unrelated, unredacted);
        var postCaptureBefore = await ReadAsync(postCapture.AuditIdentifier);
        var unrelatedBefore = await ReadAsync(unrelated.AuditIdentifier);

        await RunAsync(apply: true);

        (await ReadAsync(postCapture.AuditIdentifier)).ShouldBe(postCaptureBefore, "payload text, size and version, including DetailChanged");
        (await ReadAsync(unrelated.AuditIdentifier)).ShouldBe(unrelatedBefore, "a row of an unrelated kind is never read, even with a Url member");
        (await ReadAsync(unredacted.AuditIdentifier)).Version.ShouldBe(2, "premise: the step ran and did rewrite its own population");
    }

    private async Task<CapturingLogger> RunAsync(bool apply)
    {
        CapturingLogger logger = new();
        await new AuditUrlRedactionStep(
                new ConnectionStringDbContextFactory(await ConnectionStringAsync()),
                Options.Create(new AuditUrlRedactionOptions { Apply = apply }),
                logger)
            .RunAsync(default);

        return logger;
    }

    private async Task SeedAsync(params Seed[] seeds)
    {
        await using var context = await aspire.CreateAuditObservabilityDbContextAsync();

        foreach (var seed in seeds)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO audit_events (
                    audit_id, occurred_at, received_at, fab_id, event_kind,
                    resource_kind, resource_identifier, actor_identifier,
                    actor_username, event_identifier, payload, payload_size_bytes,
                    schema_version)
                VALUES (
                    {seed.AuditIdentifier}, {seed.OccurredAt}, {DateTimeOffset.UtcNow}, NULL,
                    {seed.Kind}, NULL, NULL, {Guid.Empty}, NULL,
                    {Guid.CreateVersion7()}, {seed.Payload}::jsonb, {seed.Size},
                    {(short)seed.SchemaVersion})
                """);
        }
    }

    private async Task<StoredRow> ReadAsync(Guid auditIdentifier)
    {
        await using var context = await aspire.CreateAuditObservabilityDbContextAsync();

        var rows = await context.Database
            .SqlQuery<StoredRow>($"""
                SELECT payload::text AS "Payload",
                       payload_size_bytes AS "Size",
                       schema_version::int AS "Version"
                FROM audit_events
                WHERE audit_id = {auditIdentifier}
                """)
            .ToListAsync();

        return rows.ShouldHaveSingleItem();
    }

    private async Task CompressChunkContainingAsync(DateTimeOffset instant)
    {
        await using var context = await aspire.CreateAuditObservabilityDbContextAsync();

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT compress_chunk(format('%I.%I', chunk_schema, chunk_name)::regclass, if_not_compressed => true)
            FROM timescaledb_information.chunks
            WHERE hypertable_name = 'audit_events'
              AND range_start <= {instant} AND {instant} < range_end
            """);
    }

    private async Task<bool> ChunkIsCompressedAsync(DateTimeOffset instant)
    {
        await using var context = await aspire.CreateAuditObservabilityDbContextAsync();

        var flags = await context.Database
            .SqlQuery<bool>($"""
                SELECT is_compressed AS "Value"
                FROM timescaledb_information.chunks
                WHERE hypertable_name = 'audit_events'
                  AND range_start <= {instant} AND {instant} < range_end
                """)
            .ToListAsync();

        return flags.ShouldHaveSingleItem();
    }

    private async Task<string> ConnectionStringAsync() =>
        await aspire.App.GetConnectionStringAsync(AspireFixture.AuditObservabilityConnectionName)
        ?? throw new InvalidOperationException(
            $"Connection string '{AspireFixture.AuditObservabilityConnectionName}' was not provisioned by Aspire.");

    private sealed class ConnectionStringDbContextFactory(string connectionString)
        : IDbContextFactory<AuditObservabilityDbContext>
    {
        public AuditObservabilityDbContext CreateDbContext()
        {
            DbContextOptionsBuilder<AuditObservabilityDbContext> optionsBuilder = new();
            optionsBuilder.UseNpgsql(connectionString);
            return new AuditObservabilityDbContext(optionsBuilder.Options);
        }
    }

    private sealed record LogEntry(string Message, IReadOnlyDictionary<string, object?> State);

    private sealed class CapturingLogger : ILogger<AuditUrlRedactionStep>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = (state as IEnumerable<KeyValuePair<string, object?>> ?? [])
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            Entries.Add(new(formatter(state, exception), fields));
        }
    }
}
