namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// How far ahead of this service's clock an address change's own
/// <c>ChangedAt</c> may be before it is clamped (spec 385, #2991 addendum).
/// </summary>
public sealed class StreamRepointOptions
{
    public const string SectionName = "StreamRepoint";

    /// <summary>
    /// Ordering trusts the stamp the publisher wrote, so a stamp far in the
    /// future would pin a stream against every later legitimate change. A
    /// stamp further ahead than this is clamped to the receiver's now. The
    /// default absorbs ordinary clock skew between CameraCatalog and this
    /// service.
    /// </summary>
    public TimeSpan ChangedAtTolerance { get; set; } = TimeSpan.FromMinutes(5);
}
