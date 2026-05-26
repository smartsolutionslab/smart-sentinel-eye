using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;

/// <summary>
/// Scripted MediaMTX HTTP API. Records calls so tests can assert
/// idempotency + ordering, and lets tests set <see cref="OnAddPath"/>
/// to throw and simulate the gateway being unreachable.
/// </summary>
public sealed class FakeRtspGateway : IRtspGateway
{
    private readonly Dictionary<MediaMtxPath, RtspPathHealth> _paths = new();
    public List<(MediaMtxPath Path, string Source)> AddCalls { get; } = new();
    public List<MediaMtxPath> RemoveCalls { get; } = new();
    public Action<MediaMtxPath, string> OnAddPath { get; set; } = (_, _) => { };

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
        RemoveCalls.Add(path);
        _paths.Remove(path);
        return Task.CompletedTask;
    }

    public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken)
    {
        if (_paths.TryGetValue(path, out RtspPathHealth health))
        {
            return Task.FromResult(health);
        }
        return Task.FromResult(new RtspPathHealth(
            IsReady: false,
            LastError: "path not found",
            LastFrameAt: null,
            DetectedMode: TranscodeMode.Unknown));
    }
}
