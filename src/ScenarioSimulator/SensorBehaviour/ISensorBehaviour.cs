using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.SensorBehaviour;

/// <summary>
/// A sensor's value-over-dwell curve (spec 010 / ADR-0111 M2). The named
/// strategy is code; its numeric parameters are config
/// (<see cref="SensorDefinition"/>). <paramref name="fraction"/> is the
/// position within the station dwell (0..1); <paramref name="random"/> is
/// injected so jitter/spikes are seedable and testable.
/// </summary>
public interface ISensorBehaviour
{
    /// <summary>The behaviour name matched against <see cref="SensorDefinition.Behaviour"/>.</summary>
    string Name { get; }

    double Sample(SensorDefinition sensor, double fraction, Random random);
}
