using System.Text;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// The size of an audit row's payload, in UTF-8 bytes.
///
/// <para>
/// <b>Not covered by the payload exemption.</b> ADR-0139 exempts a captured
/// payload from being <i>parsed</i>, not from having a type, and this is a
/// measure derived from the payload rather than the opaque body itself — so
/// the constitution §II ban on a bare <c>int</c> applies to it as to anything
/// else.
/// </para>
///
/// <para>
/// Zero is admissible here even though <see cref="AuditPayload"/> refuses an
/// empty body. The non-emptiness invariant belongs to the payload and is
/// enforced there; duplicating it would put one rule in two places, and the
/// only thing a byte count can be on its own is non-negative.
/// </para>
/// </summary>
public readonly record struct PayloadSizeBytes(int Value) : IValueObject<int>
{
    public static PayloadSizeBytes From(int value)
    {
        Ensure.That(value).AtLeast(0);
        return new(value);
    }

    /// <summary>Measures <paramref name="payload"/> as it is stored — UTF-8 bytes, not chars.</summary>
    public static PayloadSizeBytes Of(string payload)
    {
        Ensure.That(payload).IsNotNull();
        return new(Encoding.UTF8.GetByteCount(payload));
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
