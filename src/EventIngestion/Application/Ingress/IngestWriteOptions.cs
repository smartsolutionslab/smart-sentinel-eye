namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// Configuration shape for <see cref="IngestWriteLimiter"/>'s concurrency
/// (spec 175, issue #2212).
///
/// <para>
/// Configurable because a fab's write throughput is a property of its own
/// deployment, not a constant every fab shares; the shipped default stays
/// unchanged so an operator who sets nothing observes today's behaviour.
/// </para>
/// </summary>
public sealed class IngestWriteOptions
{
    public const string SectionName = "EventIngestion:IngestWrite";

    public int Concurrency { get; set; } = IngestWriteLimiter.DefaultConcurrency;
}
