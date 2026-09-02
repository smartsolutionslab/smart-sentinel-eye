using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// The version of the audit-row schema a row was written under.
///
/// <para>
/// <b>A <c>short</c> was legal until ADR-0140</b>, which is the only reason
/// this type is younger than the property it wraps: §II's banned list named
/// nine types and omitted <c>short</c> while banning <c>int</c> and
/// <c>long</c>, so a bare numeric primitive sat on this aggregate by clerical
/// accident rather than by decision.
/// </para>
///
/// <para>
/// Guarded for non-negativity and nothing more. Which versions exist is a fact
/// about this system's history, not an invariant of the type — a guard reading
/// <c>== 1</c> would refuse the second version this type exists to
/// distinguish, and it would do so in the reader that has to open old rows.
/// </para>
/// </summary>
public readonly record struct SchemaVersion(short Value) : IValueObject<short>
{
    /// <summary>The version this build stamps on every row it writes.</summary>
    public static readonly SchemaVersion Current = new(1);

    public static SchemaVersion From(short value)
    {
        Ensure.That(value).AtLeast(0);
        return new(value);
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
