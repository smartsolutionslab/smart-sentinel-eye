using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        ScenarioOptions options = scenarioOptions.Value;
        if (!options.Scenarios.TryGetValue(options.Active, out ScenarioDefinition scenario)
            || scenario.Timeline is null
            || scenario.Assets.Count == 0)
        {
            return;
        }

        await publisher.StartAsync(stoppingToken);

        TimelineDefinition timeline = scenario.Timeline;
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.BilletRunStarted(scenario.Assets.Count, timeline.DwellMs, timeline.TickMs);

            foreach (AssetDefinition asset in scenario.Assets)
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

        int tick = Math.Max(timeline.TickMs, 1);
        int dwell = Math.Max(timeline.DwellMs, tick);

        for (int elapsed = 0; elapsed < dwell && !cancellationToken.IsCancellationRequested; elapsed += tick)
        {
            double fraction = (double)elapsed / dwell;

            foreach (SensorDefinition sensor in asset.Sensors)
            {
                if (!behaviours.TryGetValue(sensor.Behaviour, out ISensorBehaviour behaviour))
                {
                    continue;
                }

                double value = behaviour.Sample(sensor, fraction, random);
                MqttSample sample = mapper.Map(asset, sensor, value);
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
