using Microsoft.Extensions.Options;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// The receiver-side rule for how far an address change's own
/// <c>ChangedAt</c> is trusted (spec 385, #2991 addendum): a stamp further
/// ahead of this service's clock than the tolerance is clamped to now, and a
/// recorded stamp beyond that same bound is treated as poisoned.
/// </summary>
public sealed class ChangedAtPolicy(
    IClock clock,
    IOptions<StreamRepointOptions> options)
{
    /// <summary>
    /// The clock the policy judges against, exposed so the handler can
    /// pass the same clock to the aggregate's repoint.
    /// </summary>
    public IClock Clock { get; } = clock;

    public ChangedAtClamp Clamp(SourceChangedAt incoming)
    {
        Ensure.That(incoming).IsNotNull();

        var now = Clock.UtcNow;

        // Clamped rather than rejected: a genuine change from a skewed clock
        // must still apply, it just must not outrank every later one.
        return incoming.Value <= now + options.Value.ChangedAtTolerance
            ? new(incoming, false)
            : new(SourceChangedAt.From(now), true);
    }

    public bool IsPoisoned(SourceChangedAt recorded)
    {
        Ensure.That(recorded).IsNotNull();

        return recorded.Value > Clock.UtcNow + options.Value.ChangedAtTolerance;
    }
}

public sealed record ChangedAtClamp(
    SourceChangedAt Applied,
    bool WasClamped);
