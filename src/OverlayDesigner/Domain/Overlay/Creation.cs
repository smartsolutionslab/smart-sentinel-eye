using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// When an overlay was created, and by whom — spec 058. Used twice here: once on the
/// chain and once on each <see cref="Revision"/>, which is the only place in
/// this feature where a composite sits inside an owned collection.
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
/// A revision also carries <c>PublishedAt</c> and <c>ArchivedAt</c>, and neither
/// has an actor beside it. The asymmetry is left visible rather than papered
/// over: this system records who created a revision and not who published it
/// (FR-010).
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
