using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

/// <summary>
/// In-memory IStreamRepository for handler tests (ADR-0052). Behaves like
/// the real repository within process; no EF, no Postgres, no transactions.
/// </summary>
/// <remarks>
/// <para>
/// Spec 318 §6.1. <see cref="IsUnchangedSinceLoadAsync"/> and
/// <see cref="ReadCommittedStateAsync"/> are declared as plain public methods
/// before <c>IStreamRepository</c> carries them, so this class compiles
/// today and starts satisfying the interface the moment phase 4b adds the
/// two members with these exact signatures (spec 318 §3) — no edit needed
/// here.
/// </para>
/// <para>
/// <c>committedVersions</c>/<c>loadedVersions</c> model the version token a
/// real <c>UPDATE ... WHERE version = @loaded</c> would check, without a
/// database: every <c>Get*Async</c> hit records what this unit of work
/// loaded; every <see cref="SaveAsync"/> advances the committed value for
/// every id loaded since the last save (mirroring the real interceptor
/// bumping a dirty root). <see cref="IsUnchangedSinceLoadAsync"/> compares
/// the two.
/// </para>
/// <para>
/// <b>Known limitation (by design — spec 318 plan §6.1).</b> Instances are
/// shared between handlers built on the same fake, so a handler that
/// mistakenly decides on the tracked <c>Stream.State</c> instead of calling
/// <see cref="ReadCommittedStateAsync"/> sees the same mutated object either
/// way and this fake cannot tell the difference. Only a real second
/// <c>DbContext</c> (spec 318 plan §6.3, IT-5/IT-6) can.
/// </para>
/// </remarks>
public sealed class InMemoryStreamRepository : IStreamRepository
{
    private readonly List<Domain.Stream.Stream> streams = [];
    private readonly List<Domain.Stream.Stream> pendingAdds = [];
    private readonly Dictionary<StreamIdentifier, int> committedVersions = [];
    private readonly Dictionary<StreamIdentifier, int> loadedVersions = [];
    private readonly HashSet<StreamIdentifier> loadedSinceSave = [];
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
        RecordLoad(found);
        return Task.FromResult(found is null
            ? Option<Domain.Stream.Stream>.None
            : Option<Domain.Stream.Stream>.Some(found));
    }

    public Task<Option<Domain.Stream.Stream>> GetByCameraAsync(CameraIdentifier camera, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream? found = streams.FirstOrDefault(candidate => candidate.Camera.Equals(camera));
        RecordLoad(found);
        return Task.FromResult(found is null
            ? Option<Domain.Stream.Stream>.None
            : Option<Domain.Stream.Stream>.Some(found));
    }

    public Task<Option<Domain.Stream.Stream>> GetByPathAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream? found = streams.FirstOrDefault(candidate => candidate.Path.Equals(path));
        RecordLoad(found);
        return Task.FromResult(found is null
            ? Option<Domain.Stream.Stream>.None
            : Option<Domain.Stream.Stream>.Some(found));
    }

    public void Add(Domain.Stream.Stream stream) => pendingAdds.Add(stream);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        OnSave();

        HashSet<StreamIdentifier> justAdded = pendingAdds.Select(stream => stream.Id).ToHashSet();

        foreach (Domain.Stream.Stream stream in pendingAdds)
        {
            committedVersions[stream.Id] = 0;
            loadedVersions[stream.Id] = 0;
        }

        streams.AddRange(pendingAdds);
        pendingAdds.Clear();

        foreach (StreamIdentifier id in loadedSinceSave)
        {
            if (justAdded.Contains(id))
            {
                continue;
            }

            committedVersions[id] = committedVersions.GetValueOrDefault(id) + 1;
        }

        loadedSinceSave.Clear();
        SaveCallCount++;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reports whether the stream's row still carries the version this unit
    /// of work loaded or inserted (spec 318 §3). See the class remarks for
    /// what this fake cannot catch.
    /// </summary>
    public Task<bool> IsUnchangedSinceLoadAsync(Domain.Stream.Stream stream, CancellationToken cancellationToken)
    {
        bool unchanged = loadedVersions.TryGetValue(stream.Id, out int loaded)
            && committedVersions.TryGetValue(stream.Id, out int committed)
            && loaded == committed;

        return Task.FromResult(unchanged);
    }

    /// <summary>
    /// The row's committed state — here, the shared stored instance's state
    /// (spec 318 §3; see the class remarks on why that sharing is a known gap).
    /// </summary>
    public Task<StreamState> ReadCommittedStateAsync(StreamIdentifier stream, CancellationToken cancellationToken)
    {
        Domain.Stream.Stream found = streams.Single(candidate => candidate.Id.Equals(stream));
        return Task.FromResult(found.State);
    }

    private void RecordLoad(Domain.Stream.Stream? found)
    {
        if (found is null)
        {
            return;
        }

        loadedVersions[found.Id] = committedVersions.GetValueOrDefault(found.Id);
        loadedSinceSave.Add(found.Id);
    }
}
