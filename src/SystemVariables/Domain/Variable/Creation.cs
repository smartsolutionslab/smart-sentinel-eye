using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// When a variable was defined, and by whom — spec 058.
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
/// A variable records no archival moment at all — archiving moves
/// <c>State</c> and stamps nothing — so unlike the other three contexts there
/// is no bare lifecycle timestamp sitting beside this composite. Nothing to
/// group and nothing left ungrouped.
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
