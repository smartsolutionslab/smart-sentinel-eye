using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

/// <summary>
/// Scripted MediaMTX HTTP API. Records calls so tests can assert
/// idempotency + ordering, and lets tests set <see cref="OnAddPath"/>
/// to throw and simulate the gateway being unreachable.
/// </summary>
public sealed class FakeRtspGateway : IRtspGateway
{
    private readonly Dictionary<MediaMtxPath, RtspPathHealth> _paths = [];
    public List<(MediaMtxPath Path, string Source)> AddCalls { get; } = [];
    public List<MediaMtxPath> RemoveCalls { get; } = [];
    public Action<MediaMtxPath, string> OnAddPath { get; set; } = (_, _) => { };

    /// <summary>
    /// Lets a test make path removal fail — the SFU unreachable while a camera
    /// is retired (spec 028 FR-008a). Invoked before the call is recorded, so a
    /// throwing hook leaves RemoveCalls empty, as a failed call would.
    /// </summary>
    public Action<MediaMtxPath> OnRemovePath { get; set; } = _ => { };

    public List<(MediaMtxPath Path, string Source)> RepointCalls { get; } = [];

    /// <summary>Lets a test make the SFU unreachable during a re-point (spec 029 FR-013a).</summary>
    public Action<MediaMtxPath, string> OnRepointPath { get; set; } = (_, _) => { };

    public Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken)
    {
        OnAddPath(path, rtspSourceUrl);
        AddCalls.Add((path, rtspSourceUrl));
        _paths[path] = new RtspPathHealth(
            IsReady: true,
            LastError: null,
            LastFrameAt: null,
            DetectedMode: TranscodeMode.Passthrough);
        return Task.CompletedTask;
    }

    public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        OnRemovePath(path);
        RemoveCalls.Add(path);
        _paths.Remove(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Changes an existing path's source in place, as MediaMTX's patch
    /// endpoint does. Records the call so a test can assert the SFU was told
    /// the new address, not merely that the aggregate holds it.
    /// </summary>
    public Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken)
    {
        OnRepointPath(path, rtspSourceUrl);
        RepointCalls.Add((path, rtspSourceUrl));

        if (_paths.ContainsKey(path))
        {
            _paths[path] = new RtspPathHealth(
                IsReady: true,
                LastError: null,
                LastFrameAt: null,
                DetectedMode: TranscodeMode.Passthrough);
        }

        return Task.CompletedTask;
    }

    public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        if (_paths.TryGetValue(path, out RtspPathHealth? health))
        {
            return Task.FromResult(health);
        }
        return Task.FromResult(new RtspPathHealth(
            IsReady: false,
            LastError: "path not found",
            LastFrameAt: null,
            DetectedMode: TranscodeMode.Unknown));
    }

    public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MediaMtxPath>>(_paths.Keys.ToArray());
}
