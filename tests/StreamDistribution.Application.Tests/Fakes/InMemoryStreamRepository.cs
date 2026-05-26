using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

/// <summary>
/// In-memory IStreamRepository for handler tests (ADR-0052). Behaves like
/// the real repository within process; no EF, no Postgres, no transactions.
/// </summary>
public sealed class InMemoryStreamRepository : IStreamRepository
{
    private readonly List<Domain.Stream.Stream> _streams = new();
    private readonly List<Domain.Stream.Stream> _pendingAdds = new();
    public int SaveCallCount { get; private set; }

    public IReadOnlyList<Domain.Stream.Stream> Streams => _streams;

    public Task<Option<Domain.Stream.Stream>> GetByIdentifierAsync(StreamIdentifier stream, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream found = _streams.FirstOrDefault(candidate => candidate.Id.Equals(stream));
        return Task.FromResult(found is null
            ? Option<Domain.Stream.Stream>.None
            : Option<Domain.Stream.Stream>.Some(found));
    }

    public Task<Option<Domain.Stream.Stream>> GetByCameraAsync(CameraIdentifier camera, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream found = _streams.FirstOrDefault(candidate => candidate.Camera.Equals(camera));
        return Task.FromResult(found is null
            ? Option<Domain.Stream.Stream>.None
            : Option<Domain.Stream.Stream>.Some(found));
    }

    public Task<Option<Domain.Stream.Stream>> GetByPathAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream found = _streams.FirstOrDefault(candidate => candidate.Path.Equals(path));
        return Task.FromResult(found is null
            ? Option<Domain.Stream.Stream>.None
            : Option<Domain.Stream.Stream>.Some(found));
    }

    public void Add(Domain.Stream.Stream stream) => _pendingAdds.Add(stream);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        _streams.AddRange(_pendingAdds);
        _pendingAdds.Clear();
        SaveCallCount++;
        return Task.CompletedTask;
    }
}
