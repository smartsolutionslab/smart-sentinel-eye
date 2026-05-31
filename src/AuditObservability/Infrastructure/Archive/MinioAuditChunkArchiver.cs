using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using SmartSentinelEye.AuditObservability.Application.Retention;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Archive;

/// <summary>
/// Production <see cref="IAuditChunkArchiver"/>: streams every
/// row in the chunk to a gzipped NDJSON object on MinIO, with
/// an <c>Content-MD5</c> checksum verified post-upload against
/// the object's ETag.
///
/// <para>
/// Idempotent: if an object with the expected key already
/// exists and its ETag matches the freshly-computed MD5, the
/// archiver short-circuits with
/// <see cref="ChunkArchiveResult.AlreadyArchived"/> set —
/// safe to re-run after a mid-flight failure.
/// </para>
/// </summary>
public sealed class MinioAuditChunkArchiver(
    IMinioClient minio,
    IDbContextFactory<AuditObservabilityDbContext> dbContextFactory,
    IOptions<MinioOptions> options,
    ILogger<MinioAuditChunkArchiver> logger) : IAuditChunkArchiver
{
    public async Task<ChunkArchiveResult> ArchiveChunkAsync(
        AuditChunk chunk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        MinioOptions opts = options.Value;
        await EnsureBucketAsync(opts.Bucket, cancellationToken).ConfigureAwait(false);

        string objectKey = BuildObjectKey(opts.ObjectKeyTemplate, chunk);

        await using AuditObservabilityDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        List<AuditEventEntity> rows = await context.AuditEvents
            .Where(auditEvent => auditEvent.OccurredAt >= chunk.OccurredFrom && auditEvent.OccurredAt < chunk.OccurredUntil)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        using MemoryStream payload = new();
        await using (GZipStream gz = new(payload, CompressionLevel.Optimal, leaveOpen: true))
        await using (StreamWriter writer = new(gz))
        {
            foreach (AuditEventEntity row in rows)
            {
                await writer.WriteLineAsync(
                    JsonSerializer.Serialize(MinioAuditRow.From(row))).ConfigureAwait(false);
            }
        }

        payload.Position = 0;
#pragma warning disable CA5351 // S3's Content-MD5 header is an integrity check, not a security primitive.
        string contentMd5 = Convert.ToHexStringLower(MD5.HashData(payload.ToArray()));
#pragma warning restore CA5351

        // Idempotency: a previous successful run leaves the
        // object in place; only re-upload if it's missing or
        // the checksum drifted.
        string? existingEtag = await TryGetEtagAsync(opts.Bucket, objectKey, cancellationToken)
            .ConfigureAwait(false);
        if (existingEtag is not null && string.Equals(existingEtag, contentMd5, StringComparison.OrdinalIgnoreCase))
        {
            Log.ChunkAlreadyArchived(logger, chunk.ChunkIdentifier, objectKey);
            return new ChunkArchiveResult(objectKey, contentMd5, rows.Count, AlreadyArchived: true);
        }

        payload.Position = 0;
        await minio.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(opts.Bucket)
                .WithObject(objectKey)
                .WithStreamData(payload)
                .WithObjectSize(payload.Length)
                .WithContentType("application/x-ndjson")
                .WithHeaders(new Dictionary<string, string>
                {
#pragma warning disable CA5351
                    ["Content-MD5"] = Convert.ToBase64String(MD5.HashData(payload.ToArray())),
#pragma warning restore CA5351
                }),
            cancellationToken).ConfigureAwait(false);

        Log.ArchivedAuditChunk(logger, chunk.ChunkIdentifier, rows.Count, objectKey);

        return new ChunkArchiveResult(objectKey, contentMd5, rows.Count, AlreadyArchived: false);
    }

    private async Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        bool exists = await minio.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucket), cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            await minio.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(bucket), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> TryGetEtagAsync(
        string bucket, string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            var stat = await minio.StatObjectAsync(
                new StatObjectArgs().WithBucket(bucket).WithObject(objectKey),
                cancellationToken).ConfigureAwait(false);
            return stat.ETag?.Trim('"');
        }
        catch (ObjectNotFoundException)
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

    private sealed record MinioAuditRow(
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
        public static MinioAuditRow From(AuditEventEntity row) => new(
            row.Id.Value, row.OccurredAt, row.ReceivedAt,
            row.Fab?.Value, row.EventKind.Value,
            row.ResourceKind?.Value, row.ResourceIdentifier?.Value,
            row.Actor.Value, row.ActorUsername,
            row.EventIdentifier.Value, row.Payload, row.SchemaVersion);
    }
}
