using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Automation.Domain.Rule;

/// <summary>
/// When a rule was created, and by whom — spec 058.
///
/// <para>
/// Three other contexts declare a <c>Creation</c> with the same shape, and the
/// duplication is deliberate (§III, FR-002): each context's
/// <see cref="CreatedAt"/> is its own type, and one shared composite would
/// either cross a context boundary or take a bare <c>DateTimeOffset</c> and
/// undo spec 057.
/// </para>
///
/// <para>
/// A rule also carries <c>PublishedAt</c> and <c>ArchivedAt</c>, and neither has
/// an actor beside it. That asymmetry is left visible rather than papered over:
/// this system records who created a rule and not who published it (FR-010).
/// </para>
/// </summary>
public sealed record Creation(CreatedAt At, OperatorIdentifier By) : IValueObject
{
    public static Creation From(CreatedAt at, OperatorIdentifier by)
    {
        Ensure.That(at).IsNotNull();
        return new(at, by);
    }
}
