using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using SmartSentinelEye.AuditObservability.Application.Retention;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Archive;

/// <summary>
/// Production <see cref="IAuditChunkArchiver"/>: streams every
/// row in the chunk to a gzipped NDJSON blob on Azure Blob Storage
/// (Azurite emulator in dev/CI, spec 009 ADR-0101, ADR-0155), with
/// a Content-MD5 checksum the service verifies on upload and the
/// archiver re-checks against the blob's stored content hash.
///
/// <para>
/// Idempotent: if a blob at the expected key already exists and
/// its stored content hash matches the freshly-computed MD5, the
/// archiver short-circuits with
/// <see cref="ChunkArchiveResult.AlreadyArchived"/> set —
/// safe to re-run after a mid-flight failure.
/// </para>
/// </summary>
public sealed class AzureBlobAuditChunkArchiver(
    BlobServiceClient blobServiceClient,
    IDbContextFactory<AuditObservabilityDbContext> dbContextFactory,
    IOptions<AuditArchiveOptions> options,
    ILogger<AzureBlobAuditChunkArchiver> logger) : IAuditChunkArchiver
{
    public async Task<ChunkArchiveResult> ArchiveChunkAsync(
        AuditChunk chunk, CancellationToken cancellationToken)
    {
        Ensure.That(chunk).IsNotNull();

        AuditArchiveOptions opts = options.Value;
        BlobContainerClient container = blobServiceClient.GetBlobContainerClient(opts.ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        string objectKey = BuildObjectKey(opts.ObjectKeyTemplate, chunk);
        BlobClient blob = container.GetBlobClient(objectKey);

        await using AuditObservabilityDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);

        List<AuditEventEntity> rows = await context.AuditEvents
            .Where(auditEvent => auditEvent.OccurredAt >= chunk.OccurredFrom && auditEvent.OccurredAt < chunk.OccurredUntil)
            .ToListAsync(cancellationToken);

        using MemoryStream payload = new();
        await using (GZipStream gz = new(payload, CompressionLevel.Optimal, leaveOpen: true))
        await using (StreamWriter writer = new(gz))
        {
            foreach (AuditEventEntity row in rows)
            {
                await writer.WriteLineAsync(
                    JsonSerializer.Serialize(BlobAuditRow.From(row)));
            }
        }

        payload.Position = 0;
#pragma warning disable CA5351, S4790 // Content-MD5 is an integrity check, not a security primitive.
        byte[] md5Bytes = MD5.HashData(payload.ToArray());
#pragma warning restore CA5351, S4790
        string contentMd5 = Convert.ToHexStringLower(md5Bytes);

        // Idempotency: a previous successful run leaves the blob
        // in place; only re-upload if it's missing or the
        // checksum drifted.
        byte[]? existingContentHash = await TryGetContentHashAsync(blob, cancellationToken);
        if (existingContentHash is not null && existingContentHash.AsSpan().SequenceEqual(md5Bytes))
        {
            logger.ChunkAlreadyArchived(chunk.ChunkIdentifier, objectKey);
            return new ChunkArchiveResult(objectKey, contentMd5, rows.Count, AlreadyArchived: true);
        }

        payload.Position = 0;
        await blob.UploadAsync(
            payload,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/x-ndjson",
                    ContentHash = md5Bytes,
                },
            },
            cancellationToken);

        logger.ArchivedAuditChunk(chunk.ChunkIdentifier, rows.Count, objectKey);

        return new ChunkArchiveResult(objectKey, contentMd5, rows.Count, AlreadyArchived: false);
    }

    private static async Task<byte[]?> TryGetContentHashAsync(BlobClient blob, CancellationToken cancellationToken)
    {
        // #1808. GetPropertiesAsync issues a HEAD, and on a first archive the
        // answer is legitimately 404 — that is what this probe is asking. The
        // HTTP instrumentation records the 404 as `error.type` before the SDK
        // turns it into a RequestFailedException, and one error span marks the
        // whole trace, so every routine archival showed up in the dashboard as
        // a failed "archive audit chunk" while the upload had in fact
        // succeeded. Re-archiving an existing chunk — the rare case — was the
        // only one that traced clean.
        //
        // Suppressed rather than enriched: the instrumentation derives span
        // status from the status code, and reliably un-setting it afterwards
        // depends on callback ordering this does not want to rely on. The cost
        // is that the probe itself no longer appears. That is the trade worth
        // making — an existence check that misses is not something anyone acts
        // on, and a span that cries failure on the happy path is worse than no
        // span at all. The upload and the surrounding journey are untouched.
        using IDisposable suppressed = SuppressInstrumentationScope.Begin();

        try
        {
            Response<BlobProperties> properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            return properties.Value.ContentHash;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static string BuildObjectKey(string template, AuditChunk chunk)
    {
        string year = chunk.OccurredFrom.UtcDateTime.Year.ToString("D4", CultureInfo.InvariantCulture);
        string month = chunk.OccurredFrom.UtcDateTime.Month.ToString("D2", CultureInfo.InvariantCulture);
        return template
            .Replace("{fab}", "_unscoped", StringComparison.Ordinal)
            .Replace("{year:0000}", year, StringComparison.Ordinal)
            .Replace("{month:00}", month, StringComparison.Ordinal)
            .Replace("{chunkId:N}", chunk.ChunkIdentifier.ToString("N"), StringComparison.Ordinal);
    }

    private sealed record BlobAuditRow(
        Guid AuditIdentifier,
        DateTimeOffset OccurredAt,
        DateTimeOffset ReceivedAt,
        string? Fab,
        string EventKind,
        string? ResourceKind,
        string? ResourceIdentifier,
        Guid ActorIdentifier,
        string? ActorUsername,
        Guid EventIdentifier,
        string Payload,
        short SchemaVersion)
    {
        public static BlobAuditRow From(AuditEventEntity row) => new(
            row.Id.Value, row.OccurredAt, row.ReceivedAt,
            row.Fab?.Value, row.EventKind.Value,
            row.ResourceKind?.Value, row.ResourceIdentifier?.Value,
            row.Actor.Value, row.ActorUsername?.Value,
            row.EventIdentifier.Value, row.Payload.Content.Value, row.SchemaVersion.Value);
    }
}
