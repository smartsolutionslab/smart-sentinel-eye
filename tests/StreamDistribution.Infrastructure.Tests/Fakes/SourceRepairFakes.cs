using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

/// <summary>
/// A stateful stand-in for MediaMTX's configuration: PATCHes change what
/// <see cref="ListConfiguredPathSourcesAsync"/> reports, so a second sweep sees
/// what the first one wrote. Hand-written (ADR-0052); hooks run at the exact
/// interleaving points the race tests need.
/// </summary>
internal sealed class SourceRepairGateway : IRtspGateway
{
    private readonly Dictionary<MediaMtxPath, string> configured = [];
    private readonly Dictionary<MediaMtxPath, Exception> failures = [];

    public IReadOnlyDictionary<MediaMtxPath, string> Configured => configured;

    public List<(MediaMtxPath Path, string Source)> Patches { get; } = [];

    public List<MediaMtxPath> Added { get; } = [];

    public List<MediaMtxPath> Removed { get; } = [];

    /// <summary>Runs after the configured sources were captured, before they are returned.</summary>
    public Action<SourceRepairGateway>? AfterListCaptured { get; set; }

    /// <summary>Runs when a PATCH arrives, before MediaMTX applies it.</summary>
    public Action<SourceRepairGateway, MediaMtxPath, string>? BeforePatchApplied { get; set; }

    /// <summary>Runs after MediaMTX applied a PATCH, before the call returns.</summary>
    public Action<SourceRepairGateway, MediaMtxPath, string>? AfterPatchApplied { get; set; }

    public void Configure(MediaMtxPath path, string source) => configured[path] = source;

    public void FailPatchesFor(MediaMtxPath path, Exception failure) => failures[path] = failure;

    public void StopFailingPatchesFor(MediaMtxPath path) => failures.Remove(path);

    public Task<IReadOnlyDictionary<MediaMtxPath, string>> ListConfiguredPathSourcesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<MediaMtxPath, string> captured = new Dictionary<MediaMtxPath, string>(configured);
        AfterListCaptured?.Invoke(this);

        return Task.FromResult(captured);
    }

    public Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken)
    {
        Patches.Add((path, rtspSourceUrl));
        BeforePatchApplied?.Invoke(this, path, rtspSourceUrl);

        if (failures.TryGetValue(path, out var failure))
        {
            throw failure;
        }

        configured[path] = rtspSourceUrl;
        AfterPatchApplied?.Invoke(this, path, rtspSourceUrl);

        return Task.CompletedTask;
    }

    public Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken)
    {
        Added.Add(path);

        return Task.CompletedTask;
    }

    public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        Removed.Add(path);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MediaMtxPath>>([.. configured.Keys]);

    public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
        throw new NotSupportedException("the source repair never reads health");

    public Task<IReadOnlyDictionary<MediaMtxPath, RtspPathHealth>> GetAllPathsHealthAsync(
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("the source repair never reads health");
}

/// <summary>
/// The database as the repair pass sees it: the non-retired streams' committed
/// sources. <see cref="Served"/> records every value handed to the pass, so a
/// test can assert nothing else was ever written.
/// </summary>
internal sealed class SourceRepairSnapshot : IStreamSourceSnapshot
{
    private readonly Dictionary<MediaMtxPath, StreamSourceUrl> live = [];

    public List<StreamSourceUrl> Served { get; } = [];

    public List<MediaMtxPath> CommittedReads { get; } = [];

    /// <summary>Runs after the live sources were captured, before they are returned.</summary>
    public Action<SourceRepairSnapshot>? AfterListCaptured { get; set; }

    /// <summary>Runs after a committed-source read captured its answer, before it is returned.</summary>
    public Action<SourceRepairSnapshot, MediaMtxPath>? AfterCommittedReadCaptured { get; set; }

    /// <summary>When set, each committed-source read answers with the next value this produces.</summary>
    public Func<MediaMtxPath, int, Option<StreamSourceUrl>>? CommittedReadOverride { get; set; }

    public void Commit(MediaMtxPath path, string source) => live[path] = StreamSourceUrl.From(source);

    public void Retire(MediaMtxPath path) => live.Remove(path);

    public StreamSourceUrl Committed(MediaMtxPath path) => live[path];

    public Task<IReadOnlyDictionary<MediaMtxPath, StreamSourceUrl>> ListLiveSourcesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<MediaMtxPath, StreamSourceUrl> captured =
            new Dictionary<MediaMtxPath, StreamSourceUrl>(live);
        Served.AddRange(captured.Values);
        AfterListCaptured?.Invoke(this);

        return Task.FromResult(captured);
    }

    public Task<Option<StreamSourceUrl>> ReadCommittedSourceAsync(
        MediaMtxPath path,
        CancellationToken cancellationToken)
    {
        CommittedReads.Add(path);
        Option<StreamSourceUrl> answer;
        if (CommittedReadOverride is { } overridden)
        {
            answer = overridden(path, CommittedReads.Count);
        }
        else if (live.TryGetValue(path, out var source))
        {
            answer = Option<StreamSourceUrl>.Some(source);
        }
        else
        {
            answer = Option<StreamSourceUrl>.None;
        }

        if (answer.HasValue)
        {
            Served.Add(answer.Value);
        }

        AfterCommittedReadCaptured?.Invoke(this, path);

        return Task.FromResult(answer);
    }
}
