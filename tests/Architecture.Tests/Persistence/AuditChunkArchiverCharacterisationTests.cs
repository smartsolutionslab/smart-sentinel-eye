using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.Retention;
using SmartSentinelEye.AuditObservability.Infrastructure.Archive;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 352, ADR-0144), captured green on unmodified <c>develop</c> before
/// <c>AzureBlobAuditChunkArchiver.ArchiveChunkAsync</c> is split. The archiver gates a
/// destructive step: the retention sweep drops a TimescaleDB chunk only after this returned, so
/// the contract that matters is that it returns only once the blob holds the content (or its
/// stored hash matched) and that every failure propagates instead of being swallowed. Every
/// branch is pinned: the order of calls against the storage client, the object key, the
/// container name, the SELECT and its window, the decompressed body, the upload headers, the
/// result, and the log line each branch writes.
///
/// <para>
/// <b>No Azure, no database.</b> The real archiver runs over hand-written subclasses of the
/// storage clients that record calls in order, a context factory whose connection open is
/// suppressed and whose reader is a <see cref="DataTable"/>, and a recording logger. There is no
/// <c>*.Infrastructure.Tests</c> project for this context (spec 352 J3).
/// </para>
///
/// <para>
/// <b>One declared relation (spec 352 J).</b> The gzip bytes depend on the runtime's zlib, so
/// their MD5 cannot be a literal. The uploaded <c>ContentHash</c> is asserted to be the MD5 of the
/// uploaded bytes and the result's <c>ContentMd5</c> its lowercase hex; everything else,
/// including the decompressed NDJSON, is a literal. The NDJSON is compared with line endings
/// normalised, because <c>WriteLineAsync</c> uses the platform newline.
/// </para>
///
/// <para>
/// Counterfactuals seen to fail: always uploading (ignoring the stored hash) turns
/// <see cref="A_blob_whose_stored_hash_matches_is_not_uploaded_again"/> red; treating a 500 probe
/// as a missing blob turns <see cref="A_probe_that_fails_with_anything_but_not_found_propagates_before_any_upload"/> red.
/// </para>
/// </summary>
public class AuditChunkArchiverCharacterisationTests
{
    private const string ChunkIdentifier = "c0c0c0c0-0000-4000-8000-0000000000aa";
    private const string ContainerName = "audit-archive-under-test";
    private const string ObjectKey = "fab=_unscoped/year=2026/month=03/chunk-c0c0c0c00000400080000000000000aa.ndjson.gz";
    private const string ExpectedSelect =
        "SELECT a.audit_id, a.occurred_at, a.actor_identifier, a.actor_username, a.event_identifier, a.event_kind, "
        + "a.fab_id, a.fab_attribution, a.handler_entered_at, a.received_at, a.resource_identifier, a.resource_kind, "
        + "a.schema_version, a.written_at, a.payload, a.payload_size_bytes\n"
        + "FROM audit_events AS a\n"
        + "WHERE a.occurred_at::timestamptz >= @chunk_OccurredFrom AND a.occurred_at::timestamptz < @chunk_OccurredUntil";

    // "<Q>" stands for the six characters backslash-u-0-0-2-2: the JSON encoder escapes a quote inside the payload string
    // that way, and a unicode escape written out in a raw string literal is turned into a quote by the compiler.
    // "<Q>" stands for the six characters backslash-u-0-0-2-2, which is how the JSON encoder escapes a quote inside
    // the payload string. It is substituted at use because a literal unicode escape is easy to mangle in a source file.
    private const string ExpectedNdjson =
        """{"AuditIdentifier":"b1000000-0000-4000-8000-000000000001","OccurredAt":"2026-03-02T08:00:00+00:00","ReceivedAt":"2026-03-02T08:00:01+00:00","Fab":"munich","EventKind":"CameraRegisteredV1","ResourceKind":"camera","ResourceIdentifier":"cam-7","ActorIdentifier":"a1a1a1a1-0000-4000-8000-000000000001","ActorUsername":"alice@munich.test","EventIdentifier":"e1e1e1e1-0000-4000-8000-000000000002","Payload":"{<Q>camera<Q>:<Q>cam-7<Q>}","SchemaVersion":1}"""
        + "\n"
        + """{"AuditIdentifier":"b2000000-0000-4000-8000-000000000003","OccurredAt":"2026-03-03T09:30:00+00:00","ReceivedAt":"2026-03-03T09:30:02+00:00","Fab":null,"EventKind":"OverlayPublishedV1","ResourceKind":null,"ResourceIdentifier":null,"ActorIdentifier":"00000000-0000-0000-0000-000000000000","ActorUsername":null,"EventIdentifier":"e2e2e2e2-0000-4000-8000-000000000004","Payload":"{}","SchemaVersion":1}"""
        + "\n";


    private static readonly AuditChunk Chunk = new(
        Guid.Parse(ChunkIdentifier),
        DateTimeOffset.Parse("2026-03-01T00:00:00Z", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-03-08T00:00:00Z", CultureInfo.InvariantCulture));

    private sealed class Calls
    {
        public List<string> Entries { get; } = [];
    }

    private sealed class RecordingLogger(Calls calls) : ILogger<AzureBlobAuditChunkArchiver>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            calls.Entries.Add($"log {logLevel}: {formatter(state, exception)}");
    }

    private sealed class FakeServiceClient(Calls calls, BlobContainerClient container) : BlobServiceClient
    {
        public override BlobContainerClient GetBlobContainerClient(string blobContainerName)
        {
            calls.Entries.Add($"GetBlobContainerClient({blobContainerName})");
            return container;
        }
    }

    private sealed class FakeContainerClient(Calls calls, BlobClient blob) : BlobContainerClient
    {
        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default)
        {
            calls.Entries.Add("CreateIfNotExists");
            return Task.FromResult(
                Response.FromValue(BlobsModelFactory.BlobContainerInfo(ETag.All, DateTimeOffset.UnixEpoch), null!));
        }

        public override BlobClient GetBlobClient(string blobName)
        {
            calls.Entries.Add($"GetBlobClient({blobName})");
            return blob;
        }
    }

    private sealed class FakeBlobClient(Calls calls) : BlobClient
    {
        public Exception? ProbeFailsWith { get; set; }

        public Exception? UploadFailsWith { get; set; }

        public byte[]? StoredHash { get; set; }

        public bool BlobPresent { get; set; }

        public byte[]? UploadedBytes { get; private set; }

        public string? UploadedContentType { get; private set; }

        public byte[]? UploadedContentHash { get; private set; }

        public override Task<Response<BlobProperties>> GetPropertiesAsync(
            BlobRequestConditions? conditions = null,
            CancellationToken cancellationToken = default)
        {
            calls.Entries.Add("GetProperties");
            if (ProbeFailsWith is not null)
            {
                throw ProbeFailsWith;
            }

            if (!BlobPresent)
            {
                throw new RequestFailedException(404, "The specified blob does not exist.");
            }

            return Task.FromResult(
                Response.FromValue(BlobsModelFactory.BlobProperties(contentHash: StoredHash), null!));
        }

        public override async Task<Response<BlobContentInfo>> UploadAsync(
            Stream content,
            BlobUploadOptions options,
            CancellationToken cancellationToken = default)
        {
            calls.Entries.Add("Upload");
            using MemoryStream copy = new();
            await content.CopyToAsync(copy, cancellationToken);
            UploadedBytes = copy.ToArray();
            UploadedContentType = options.HttpHeaders.ContentType;
            UploadedContentHash = options.HttpHeaders.ContentHash;

            return UploadFailsWith is null
                ? Response.FromValue(
                    BlobsModelFactory.BlobContentInfo(ETag.All, DateTimeOffset.UnixEpoch, null, null, null, null, 0),
                    null!)
                : throw UploadFailsWith;
        }
    }

    private sealed class SuppressOpenInterceptor : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result) => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) => new(InterceptionResult.Suppress());
    }

    private sealed class RowsInterceptor(Calls calls, Func<DataTable> table) : DbCommandInterceptor
    {
        public string? CommandText { get; private set; }

        public List<string> Parameters { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            calls.Entries.Add("SELECT");
            CommandText = command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal);
            Parameters.AddRange(command.Parameters.Cast<DbParameter>().Select(parameter =>
                $"{parameter.ParameterName}={Convert.ToString(parameter.Value, CultureInfo.InvariantCulture)}"));
            return new(InterceptionResult<DbDataReader>.SuppressWithResult(table().CreateDataReader()));
        }
    }

    private sealed class Factory(DbContextOptions<AuditObservabilityDbContext> options)
        : IDbContextFactory<AuditObservabilityDbContext>
    {
        public AuditObservabilityDbContext CreateDbContext() => new(options);
    }

    private sealed record Harness(
        AzureBlobAuditChunkArchiver Archiver,
        FakeBlobClient Blob,
        RowsInterceptor Rows,
        Calls Calls);

    private static Harness Build(Func<DataTable>? table = null)
    {
        Calls calls = new();
        FakeBlobClient blob = new(calls);
        FakeContainerClient container = new(calls, blob);
        FakeServiceClient service = new(calls, container);
        RowsInterceptor rows = new(calls, table ?? TwoRows);
        var options = new DbContextOptionsBuilder<AuditObservabilityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .AddInterceptors(new SuppressOpenInterceptor(), rows)
            .Options;
        AzureBlobAuditChunkArchiver archiver = new(
            service,
            new Factory(options),
            Options.Create(new AuditArchiveOptions { ContainerName = ContainerName }),
            new RecordingLogger(calls));
        return new Harness(archiver, blob, rows, calls);
    }

    private static DataTable TwoRows()
    {
        var table = EmptyTable();
        table.Columns.Add("audit_id", typeof(Guid));
        table.Columns.Add("occurred_at", typeof(DateTimeOffset));
        table.Columns.Add("actor_identifier", typeof(Guid));
        table.Columns.Add("actor_username", typeof(string));
        table.Columns.Add("event_identifier", typeof(Guid));
        table.Columns.Add("event_kind", typeof(string));
        table.Columns.Add("fab_id", typeof(string));
        table.Columns.Add("fab_attribution", typeof(string));
        table.Columns.Add("handler_entered_at", typeof(DateTimeOffset));
        table.Columns.Add("received_at", typeof(DateTimeOffset));
        table.Columns.Add("resource_identifier", typeof(string));
        table.Columns.Add("resource_kind", typeof(string));
        table.Columns.Add("schema_version", typeof(short));
        table.Columns.Add("written_at", typeof(DateTimeOffset));
        table.Columns.Add("payload", typeof(string));
        table.Columns.Add("payload_size_bytes", typeof(int));

        table.Rows.Add(
            Guid.Parse("b1000000-0000-4000-8000-000000000001"),
            DateTimeOffset.Parse("2026-03-02T08:00:00Z", CultureInfo.InvariantCulture),
            Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001"),
            "alice@munich.test",
            Guid.Parse("e1e1e1e1-0000-4000-8000-000000000002"),
            "CameraRegisteredV1",
            "munich",
            "Resolved",
            DBNull.Value,
            DateTimeOffset.Parse("2026-03-02T08:00:01Z", CultureInfo.InvariantCulture),
            "cam-7",
            "camera",
            (short)1,
            DBNull.Value,
            """{"camera":"cam-7"}""",
            18);
        table.Rows.Add(
            Guid.Parse("b2000000-0000-4000-8000-000000000003"),
            DateTimeOffset.Parse("2026-03-03T09:30:00Z", CultureInfo.InvariantCulture),
            Guid.Empty,
            DBNull.Value,
            Guid.Parse("e2e2e2e2-0000-4000-8000-000000000004"),
            "OverlayPublishedV1",
            DBNull.Value,
            "Unresolved",
            DBNull.Value,
            DateTimeOffset.Parse("2026-03-03T09:30:02Z", CultureInfo.InvariantCulture),
            DBNull.Value,
            DBNull.Value,
            (short)1,
            DBNull.Value,
            "{}",
            2);
        return table;
    }

    private static DataTable EmptyTable() => new();

    private static string Gunzip(byte[] bytes)
    {
        using MemoryStream source = new(bytes);
        using GZipStream gzip = new(source, CompressionMode.Decompress);
        using StreamReader reader = new(gzip, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static byte[] Md5Of(byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        hash.AppendData(bytes);
        return hash.GetHashAndReset();
    }

    private static string[] CallsBeforeTheProbe() =>
    [
        "GetBlobContainerClient(audit-archive-under-test)",
        "CreateIfNotExists",
        "GetBlobClient(" + ObjectKey + ")",
        "SELECT",
        "GetProperties",
    ];

    private static string[] CallsAfterTheProbe(params string[] following) => [.. CallsBeforeTheProbe(), .. following];

    [Fact]
    public async Task A_missing_blob_is_uploaded_with_the_ndjson_body_the_content_type_and_the_md5()
    {
        var harness = Build();

        var result = await harness.Archiver.ArchiveChunkAsync(Chunk, default);

        harness.Calls.Entries.ShouldBe(
            CallsAfterTheProbe(
                "Upload",
                "log Information: Archived audit chunk c0c0c0c0-0000-4000-8000-0000000000aa (2 rows) to " + ObjectKey + "."));
        harness.Blob.UploadedContentType.ShouldBe("application/x-ndjson");
        Gunzip(harness.Blob.UploadedBytes!).ShouldBe(ExpectedNdjson.Replace("<Q>", "\\u0022", StringComparison.Ordinal));
        harness.Blob.UploadedContentHash.ShouldBe(Md5Of(harness.Blob.UploadedBytes!));
        result.ShouldBe(
            new ChunkArchiveResult(
                ObjectKey,
                Convert.ToHexStringLower(harness.Blob.UploadedContentHash!),
                2,
                AlreadyArchived: false));
        result.ContentMd5.Length.ShouldBe(32);
    }

    [Fact]
    public async Task The_rows_are_selected_for_the_chunk_window_with_a_closed_lower_and_open_upper_bound()
    {
        var harness = Build();

        await harness.Archiver.ArchiveChunkAsync(Chunk, default);

        harness.Rows.CommandText.ShouldBe(ExpectedSelect.Replace("\r\n", "\n", StringComparison.Ordinal));
        harness.Rows.Parameters.ShouldBe(
        [
            "@chunk_OccurredFrom=03/01/2026 00:00:00 +00:00",
            "@chunk_OccurredUntil=03/08/2026 00:00:00 +00:00",
        ]);
    }

    [Fact]
    public async Task A_blob_whose_stored_hash_matches_is_not_uploaded_again()
    {
        var first = Build();
        await first.Archiver.ArchiveChunkAsync(Chunk, default);
        var storedHash = first.Blob.UploadedContentHash!;
        var second = Build();
        second.Blob.BlobPresent = true;
        second.Blob.StoredHash = storedHash;

        var result = await second.Archiver.ArchiveChunkAsync(Chunk, default);

        second.Calls.Entries.ShouldBe(
            CallsAfterTheProbe(
                "log Information: Audit chunk c0c0c0c0-0000-4000-8000-0000000000aa already archived at "
                + ObjectKey
                + "; skipping upload."));
        second.Blob.UploadedBytes.ShouldBeNull();
        result.ShouldBe(
            new ChunkArchiveResult(ObjectKey, Convert.ToHexStringLower(storedHash), 2, AlreadyArchived: true));
    }

    [Fact]
    public async Task A_blob_whose_stored_hash_differs_is_uploaded_again()
    {
        var first = Build();
        await first.Archiver.ArchiveChunkAsync(Chunk, default);
        var lastByteFlipped = (byte[])first.Blob.UploadedContentHash!.Clone();
        lastByteFlipped[^1] ^= 0xFF;
        var firstByteFlipped = (byte[])first.Blob.UploadedContentHash.Clone();
        firstByteFlipped[0] ^= 0xFF;

        foreach (var different in new[] { new byte[16], lastByteFlipped, firstByteFlipped, [0x11, 0x22] })
        {
            var harness = Build();
            harness.Blob.BlobPresent = true;
            harness.Blob.StoredHash = different;

            var result = await harness.Archiver.ArchiveChunkAsync(Chunk, default);

            result.AlreadyArchived.ShouldBeFalse();
            harness.Calls.Entries[^2].ShouldBe("Upload");
            harness.Calls.Entries[^1].ShouldStartWith("log Information: Archived audit chunk");
        }
    }

    [Fact]
    public async Task A_blob_that_exists_without_a_stored_hash_is_uploaded_again()
    {
        var harness = Build();
        harness.Blob.BlobPresent = true;
        harness.Blob.StoredHash = null;

        var result = await harness.Archiver.ArchiveChunkAsync(Chunk, default);

        result.AlreadyArchived.ShouldBeFalse();
        harness.Calls.Entries[^2].ShouldBe("Upload");
    }

    [Fact]
    public async Task A_chunk_with_no_rows_uploads_an_empty_body_and_reports_zero_rows()
    {
        var harness = Build(EmptyTable);

        var result = await harness.Archiver.ArchiveChunkAsync(Chunk, default);

        Gunzip(harness.Blob.UploadedBytes!).ShouldBe(string.Empty);
        result.RowCount.ShouldBe(0);
        harness.Calls.Entries[^1].ShouldBe(
            "log Information: Archived audit chunk c0c0c0c0-0000-4000-8000-0000000000aa (0 rows) to " + ObjectKey + ".");
    }

    [Fact]
    public async Task A_probe_that_fails_with_anything_but_not_found_propagates_before_any_upload()
    {
        var harness = Build();
        harness.Blob.ProbeFailsWith = new RequestFailedException(500, "storage unavailable");

        var thrown = await Should.ThrowAsync<RequestFailedException>(
            () => harness.Archiver.ArchiveChunkAsync(Chunk, default));

        thrown.GetType().ShouldBe(typeof(RequestFailedException));
        thrown.Status.ShouldBe(500);
        thrown.Message.ShouldBe("storage unavailable");
        harness.Calls.Entries.ShouldBe(CallsBeforeTheProbe());
        harness.Blob.UploadedBytes.ShouldBeNull();
    }

    [Fact]
    public async Task A_probe_that_fails_with_a_non_storage_exception_propagates_too()
    {
        var harness = Build();
        harness.Blob.ProbeFailsWith = new TimeoutException("probe timed out");

        var thrown = await Should.ThrowAsync<TimeoutException>(
            () => harness.Archiver.ArchiveChunkAsync(Chunk, default));

        thrown.Message.ShouldBe("probe timed out");
        harness.Calls.Entries.ShouldBe(CallsBeforeTheProbe());
    }

    [Fact]
    public async Task An_upload_that_fails_propagates_with_no_success_log_and_no_result()
    {
        var harness = Build();
        harness.Blob.UploadFailsWith = new RequestFailedException(503, "upload refused");

        var thrown = await Should.ThrowAsync<RequestFailedException>(
            () => harness.Archiver.ArchiveChunkAsync(Chunk, default));

        thrown.GetType().ShouldBe(typeof(RequestFailedException));
        thrown.Status.ShouldBe(503);
        thrown.Message.ShouldBe("upload refused");
        harness.Calls.Entries.ShouldBe(CallsAfterTheProbe("Upload"));
    }
}
