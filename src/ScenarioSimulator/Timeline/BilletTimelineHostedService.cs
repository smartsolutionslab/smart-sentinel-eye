using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Mqtt;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.SensorBehaviour;

namespace SmartSentinelEye.ScenarioSimulator.Timeline;

/// <summary>
/// Plays the billet narrative (ADR-0111 M2): a billet travels the scenario's
/// stations in order, dwelling at each and emitting its sensors' MQTT samples
/// on a tick, looping forever. Dev-only; the AppHost gates it off CI/E2E/prod.
/// </summary>
public sealed class BilletTimelineHostedService(
    IOptions<ScenarioOptions> scenarioOptions,
    IEnumerable<ISensorBehaviour> behaviours,
    MqttSampleMapper mapper,
    MqttPublisher publisher,
    ILogger<BilletTimelineHostedService> logger) : BackgroundService
{
    private readonly Dictionary<string, ISensorBehaviour> behaviours =
        behaviours.ToDictionary(behaviour => behaviour.Name, StringComparer.OrdinalIgnoreCase);
    private readonly Random random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = scenarioOptions.Value;

        // The animated scenario only — the first of Active. This timeline walks a
        // billet through one plant's stations and publishes its sensor samples,
        // so it is per-plant and stateful, and three at once is a different
        // feature. The other plants stay fully seeded and watchable, with static
        // overlays.
        //
        // Deliberate, not an oversight: quickstart §3 tells the verifier to
        // expect exactly one wall animating, so two static walls are not
        // diagnosed as a bug.
        if (!options.Scenarios.TryGetValue(options.Animated, out var scenario)
            || scenario.Timeline is null
            || scenario.Assets.Count == 0)
        {
            return;
        }

        await publisher.StartAsync(stoppingToken);

        var timeline = scenario.Timeline;
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.BilletRunStarted(scenario.Assets.Count, timeline.DwellMs, timeline.TickMs);

            foreach (var asset in scenario.Assets)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                await DwellAtStationAsync(asset, timeline, stoppingToken);
            }

            logger.BilletRunComplete(timeline.LoopGapMs);
            await DelayAsync(timeline.LoopGapMs, stoppingToken);
        }
    }

    private async Task DwellAtStationAsync(AssetDefinition asset, TimelineDefinition timeline, CancellationToken cancellationToken)
    {
        logger.BilletEnteredStation(asset.Key, asset.Camera.Path);

        var tick = Math.Max(timeline.TickMs, 1);
        var dwell = Math.Max(timeline.DwellMs, tick);

        for (var elapsed = 0; elapsed < dwell && !cancellationToken.IsCancellationRequested; elapsed += tick)
        {
            var fraction = (double)elapsed / dwell;

            foreach (var sensor in asset.Sensors)
            {
                if (!behaviours.TryGetValue(sensor.Behaviour, out var behaviour))
                {
                    continue;
                }

                var value = behaviour.Sample(sensor, fraction, random);
                var sample = mapper.Map(asset, sensor, value);
                await publisher.PublishAsync(sample.Topic, sample.Payload, cancellationToken);
                logger.BilletSampleEmitted(sample.Topic, value, sensor.Unit, sensor.Kind);
            }

            await DelayAsync(tick, cancellationToken);
        }
    }

    private static async Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(milliseconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested; unwind quietly.
        }
    }
}
