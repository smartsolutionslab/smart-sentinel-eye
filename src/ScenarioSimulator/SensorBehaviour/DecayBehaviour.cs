using SmartSentinelEye.ScenarioSimulator.Scenario;

namespace SmartSentinelEye.ScenarioSimulator.SensorBehaviour;

/// <summary>
/// Exponential fall from <c>Start</c> toward <c>Floor</c> (e.g. cooling-bed
/// temperature 980→580). The decay rate is fixed so the curve crosses its
/// trigger comfortably before the dwell ends.
/// </summary>
public sealed class DecayBehaviour : ISensorBehaviour
{
    private const double Rate = 3d;

    public string Name => "decay";

    public double Sample(SensorDefinition sensor, double fraction, Random random)
    {
        double start = sensor.Start ?? 0d;
        double floor = sensor.Floor ?? 0d;
        return floor + ((start - floor) * Math.Exp(-Rate * fraction));
    }
}
