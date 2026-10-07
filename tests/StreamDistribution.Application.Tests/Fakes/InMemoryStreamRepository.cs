using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

/// <summary>
/// In-memory IStreamRepository for handler tests (ADR-0052). Behaves like
/// the real repository within process; no EF, no Postgres, no transactions.
/// </summary>
public sealed class InMemoryStreamRepository : IStreamRepository
{
    private readonly List<Domain.Stream.Stream> streams = [];
    private readonly List<Domain.Stream.Stream> pendingAdds = [];
    public int SaveCallCount { get; private set; }

    /// <summary>
    /// Lets a test make the save itself fail — a DB blip between
    /// <c>Add</c> and <c>SaveAsync</c> (spec 309 FR-001). Invoked before the
    /// pending adds are committed, so a throwing hook leaves the row
    /// unpersisted, as a failed save would.
    /// </summary>
    public Action OnSave { get; set; } = () => { };

    public IReadOnlyList<Domain.Stream.Stream> Streams => streams;

    public Task<Option<Domain.Stream.Stream>> GetByIdentifierAsync(StreamIdentifier stream, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream? found = streams.FirstOrDefault(candidate => candidate.Id.Equals(stream));
        return Task.FromResult(found is null
            ? Option<Domain.Stream.Stream>.None
            : Option<Domain.Stream.Stream>.Some(found));
    }

    public Task<Option<Domain.Stream.Stream>> GetByCameraAsync(CameraIdentifier camera, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream? found = streams.FirstOrDefault(candidate => candidate.Camera.Equals(camera));
        return Task.FromResult(found is null
            ? Option<Domain.Stream.Stream>.None
            : Option<Domain.Stream.Stream>.Some(found));
    }

    public Task<Option<Domain.Stream.Stream>> GetByPathAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream? found = streams.FirstOrDefault(candidate => candidate.Path.Equals(path));
        return Task.FromResult(found is null
            ? Option<Domain.Stream.Stream>.None
            : Option<Domain.Stream.Stream>.Some(found));
    }

    public void Add(Domain.Stream.Stream stream) => pendingAdds.Add(stream);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        OnSave();
        streams.AddRange(pendingAdds);
        pendingAdds.Clear();
        SaveCallCount++;
        return Task.CompletedTask;
    }
}
