using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// The recorded body of an audited event. Stored in a <c>jsonb</c> column.
///
/// <para>
/// <b>Not parsed.</b> ADR-0139 exempts captured payloads from interpretation,
/// and that exemption is load-bearing here rather than merely convenient: an
/// audit row that cannot be written because its payload failed a schema check is
/// an audit row that does not exist. Validating JSON in this type would trade a
/// malformed record for a missing one, in the context whose whole purpose is
/// answering what happened.
/// </para>
///
/// <para>
/// Unbounded, matching its column, and stored exactly as given.
/// </para>
///
/// <para>
/// Emptiness is refused. <c>AuditEvent.Record</c> guarded the envelope, the
/// mapping and the clock, and never this — so an audit row could assert that
/// something happened while carrying nothing to inspect.
/// </para>
///
/// <para>
/// This is the opposite choice from <c>DeadLetter.RawPayload</c>, and the
/// difference is reachability rather than taste. That payload comes from
/// <c>Encoding.UTF8.GetString</c> over an arbitrary MQTT body, so <c>""</c> is
/// both reachable and meaningful. This one is produced by
/// <c>JsonSerializer.Serialize</c> in <c>IntegrationEventAuditHandler</c>, which
/// yields at least <c>{}</c> for any object — so an empty payload here is not a
/// malformed message but a defect upstream, and failing loudly is right.
/// </para>
/// </summary>
public sealed record AuditPayload : StringValueObject
{
    private AuditPayload(string value)
        : base(value)
    {
    }

    public static AuditPayload From(string value)
    {
        Ensure.That(value, nameof(value)).IsNotNullOrWhiteSpace();

        return new AuditPayload(value);
    }
}
