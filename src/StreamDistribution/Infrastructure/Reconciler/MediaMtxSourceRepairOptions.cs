namespace SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

/// <summary>
/// How often <see cref="MediaMtxSourceRepairSweep"/> compares MediaMTX's
/// configured sources with the database (spec 378).
/// </summary>
public sealed class MediaMtxSourceRepairOptions
{
    public const string SectionName = "MediaMtxSourceRepair";

    /// <summary>
    /// The production value: a diverged path is repaired within roughly one
    /// interval, at one control-API call per 100 paths per tick (ADR-0171).
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);
}
