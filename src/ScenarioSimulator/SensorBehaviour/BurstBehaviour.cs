using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.SensorBehaviour;

/// <summary>
/// Baseline <c>Mean ± Jitter</c> with short spikes to <c>Peak</c> (e.g. rolling
/// force). Event-list realism only — no highlight rule keys on it in v1.
/// </summary>
public sealed class BurstBehaviour : ISensorBehaviour
{
    private const double SpikeProbability = 0.15d;

    public string Name => "burst";

    public double Sample(SensorDefinition sensor, double fraction, Random random)
    {
        if (sensor.Peak.HasValue && random.NextDouble() < SpikeProbability)
        {
            return sensor.Peak.Value;
        }

        double mean = sensor.Mean ?? 0d;
        double jitter = sensor.Jitter ?? 0d;
        return mean + (((random.NextDouble() * 2d) - 1d) * jitter);
    }
}
