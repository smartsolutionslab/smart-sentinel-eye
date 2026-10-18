namespace SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

/// <summary>
/// Spec 409 (#3013): the throttle state of the source repair's whole-read
/// Warnings. A singleton, because the typed client builds a gateway per
/// resolution and a per-gateway throttle would repeat the Warning for every
/// one of them.
/// </summary>
public sealed class MediaMtxConfiguredSourcesReadWarnings(TimeProvider timeProvider)
{
    private readonly WarningRepeatGate<Outcome> gate = new(timeProvider);

    internal bool ShouldWarn(PageReadEnd end) => gate.ShouldWarn(new Outcome(end));

    internal void Clear() => gate.Clear();

    private readonly record struct Outcome(PageReadEnd End);
}
