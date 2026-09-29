using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraSim;
using SmartSentinelEye.ScenarioSimulator.Mqtt;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ScenarioSimulator.Cues;

/// <summary>
/// Publishes each clip cue to MQTT as camera-sim's playback reaches its
/// offset (spec 289 FR-004/FR-005/FR-006, plan.md §5.2). One loop per
/// camera-sim path, across <b>every</b> active scenario — not only the
/// animated one (FR-006), unlike the billet timeline. Starts
/// <see cref="MqttPublisher"/> itself, so it is a second concurrent caller of
/// <c>MqttPublisher.StartAsync</c> alongside the billet timeline's — safe
/// because <c>StartAsync</c> is race-safe (see its own doc comment).
/// </summary>
public sealed class ClipCueHostedService(
    IOptions<ScenarioOptions> scenarioOptions,
    CameraSimPathClient pathClient,
    MqttPublisher publisher,
    MqttSampleMapper mapper,
    TimeProvider clock,
    ILogger<ClipCueHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan NotReadyPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await publisher.StartAsync(stoppingToken);

        IEnumerable<Task> loops = ResolveCuedPaths().Select(entry => RunPathAsync(entry, stoppingToken));
        await Task.WhenAll(loops);
    }

    /// <summary>
    /// Every active scenario's assets whose clip has a valid manifest
    /// (FR-006). Two assets on one path is impossible — the path is unique
    /// per camera — so a plain map is safe.
    /// </summary>
    private List<CuedPath> ResolveCuedPaths()
    {
        ScenarioOptions options = scenarioOptions.Value;
        List<CuedPath> cued = [];

        foreach (string key in options.Active)
        {
            if (!options.Scenarios.TryGetValue(key, out ScenarioDefinition? scenario))
            {
                continue;
            }

            foreach (AssetDefinition asset in scenario.Assets)
            {
                ClipManifestLoadResult result = ClipManifestLoader.Load(options.ClipsDirectory, asset.Camera.Clip);
                if (result.Violations.Count > 0)
                {
                    string violations = string.Join(
                        "; ",
                        result.Violations.Select(v => v.CueIndex is { } index ? $"cue {index}: {v.Message}" : v.Message));
                    logger.ClipManifestRefused(key, asset.Key, asset.Camera.Clip, violations);
                }

                if (result.Manifest.HasValue)
                {
                    cued.Add(new CuedPath(asset.Camera.Path, asset, result.Manifest.Value));
                }
            }
        }

        return cued;
    }

    /// <summary>
    /// One path's emission loop. Failures here are scoped to this path,
    /// never letting an exception escape and take the other paths' loops
    /// down with it (mirrors <c>ScenarioSeeder</c>'s per-asset catch).
    /// </summary>
    private async Task RunPathAsync(CuedPath cued, CancellationToken stoppingToken)
    {
        PathLoopState state = new(Anchor: null, Cursor: null);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                TimeSpan delay;
                (state, delay) = await TickAsync(cued, state, stoppingToken);

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, clock, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // CameraSimPathClient.GetAsync never throws (it logs and
                // returns None itself); anything landing here is a publish,
                // mapper or scheduling fault, not a camera-sim failure, so it
                // gets its own message rather than being reported as one.
                logger.ClipCueLoopFailed(cued.Path, exception.Message);
                await Task.Delay(NotReadyPollInterval, clock, stoppingToken);
            }
        }
    }

    private async Task<(PathLoopState State, TimeSpan Delay)> TickAsync(
        CuedPath cued, PathLoopState state, CancellationToken stoppingToken)
    {
        Option<PathState> path = await pathClient.GetAsync(cued.Path, stoppingToken);
        if (!path.HasValue || !path.Value.Ready || path.Value.ReadyTime is not { } anchor)
        {
            return (state, NotReadyPollInterval);
        }

        CueCursor? cursor = state.Cursor;
        if (state.Anchor.HasValue && state.Anchor.Value != anchor)
        {
            logger.ClipAnchorChanged(cued.Path, state.Anchor.Value, anchor);
            cursor = null;
        }

        CueDecision decision = CueSchedule.Next(anchor, clock.GetUtcNow(), cued.Manifest, cursor);
        return decision switch
        {
            CueDecision.Wait wait => (new PathLoopState(anchor, cursor), Cap(wait.Delay)),
            CueDecision.Skip skip => (new PathLoopState(anchor, HandleSkip(cued.Path, skip)), TimeSpan.Zero),
            CueDecision.Emit emit => (new PathLoopState(anchor, await HandleEmitAsync(cued, emit, stoppingToken)), TimeSpan.Zero),
            _ => (new PathLoopState(anchor, cursor), NotReadyPollInterval),
        };
    }

    private static TimeSpan Cap(TimeSpan delay) => delay < MaxWait ? delay : MaxWait;

    private CueCursor HandleSkip(string path, CueDecision.Skip skip)
    {
        logger.ClipCueSkippedLate(
            path, skip.Loop, skip.Index, skip.Cue.AtMs, skip.Lateness.TotalMilliseconds, CueSchedule.LateTolerance.TotalMilliseconds);
        return skip.NextCursor;
    }

    private async Task<CueCursor> HandleEmitAsync(CuedPath cued, CueDecision.Emit emit, CancellationToken cancellationToken)
    {
        MqttSample sample = mapper.Map(cued.Asset, emit.Cue, emit.Cue.AtMs);
        await publisher.PublishAsync(sample.Topic, sample.Payload, cancellationToken);

        double latenessMs = (clock.GetUtcNow() - emit.DueAt).TotalMilliseconds;
        logger.ClipCueEmitted(cued.Path, emit.Loop, emit.Index, emit.Cue.AtMs, latenessMs);

        return emit.NextCursor;
    }

    private sealed record CuedPath(string Path, AssetDefinition Asset, ClipManifest Manifest);

    private sealed record PathLoopState(DateTimeOffset? Anchor, CueCursor? Cursor);
}
