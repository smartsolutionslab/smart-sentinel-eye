using SmartSentinelEye.AuditObservability.Application.Retention;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Fakes;

public sealed class FakeAuditChunkArchiver : IAuditChunkArchiver
{
    private readonly Dictionary<Guid, ChunkArchiveResult> _store = new();

    public List<AuditChunk> ArchivedChunks { get; } = new();
    public Exception? FailNextCall { get; set; }

    public Task<ChunkArchiveResult> ArchiveChunkAsync(
        AuditChunk chunk, CancellationToken cancellationToken)
    {
        if (FailNextCall is not null)
        {
            Exception toThrow = FailNextCall;
            FailNextCall = null;
            throw toThrow;
        }

        if (_store.TryGetValue(chunk.ChunkIdentifier, out ChunkArchiveResult? cached))
        {
            ArchivedChunks.Add(chunk);
            return Task.FromResult(cached with { AlreadyArchived = true });
        }

        ChunkArchiveResult fresh = new(
            MinioObjectKey: $"fab=_unscoped/chunk-{chunk.ChunkIdentifier:N}.ndjson.gz",
            ContentMd5: $"md5-{chunk.ChunkIdentifier:N}"[..16],
            RowCount: 42,
            AlreadyArchived: false);
        _store[chunk.ChunkIdentifier] = fresh;
        ArchivedChunks.Add(chunk);
        return Task.FromResult(fresh);
    }
}

public sealed class FakeAuditChunkInventory : IAuditChunkInventory
{
    private readonly List<AuditChunk> _chunks;

    public FakeAuditChunkInventory(IEnumerable<AuditChunk> seed) => _chunks = [.. seed];

    public List<AuditChunk> Dropped { get; } = new();

    public Task<IReadOnlyList<AuditChunk>> ListChunksOlderThanAsync(
        DateTimeOffset boundary, CancellationToken cancellationToken)
    {
        IReadOnlyList<AuditChunk> stale = _chunks
            .Where(c => c.OccurredUntil <= boundary)
            .ToList();
        return Task.FromResult(stale);
    }

    public Task DropChunkAsync(AuditChunk chunk, CancellationToken cancellationToken)
    {
        Dropped.Add(chunk);
        _chunks.RemoveAll(c => c.ChunkIdentifier == chunk.ChunkIdentifier);
        return Task.CompletedTask;
    }
}
