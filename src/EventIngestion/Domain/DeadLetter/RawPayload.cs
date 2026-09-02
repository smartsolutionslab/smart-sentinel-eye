using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

/// <summary>
/// The body of a rejected delivery, captured verbatim so an operator can
/// post-mortem it without a redeploy (spec 006 FR-015).
///
/// <para>
/// <b>Deliberately unbounded and unparsed.</b> ADR-0139 exempts captured
/// payloads from being interpreted — the column is <c>text</c>, and an invented
/// ceiling would destroy the evidence in precisely the cases most worth keeping,
/// since an oversized message is itself a common cause of rejection.
/// </para>
///
/// <para>
/// Not trimmed either. Leading and trailing whitespace can be the defect under
/// investigation, and a dead letter that quietly differs from what arrived is
/// not evidence.
/// </para>
///
/// <para>
/// <b>Empty is allowed</b>, matching <c>Capture</c>'s existing <c>IsNotNull()</c>
/// guard. An earlier draft of this type refused emptiness on the reasoning that
/// an empty payload discards the evidence. That is backwards: the topic and the
/// rejection reason are the evidence, and an empty body is itself a finding.
/// </para>
///
/// <para>
/// It is also reachable. <c>MqttSubscriberHostedService</c> builds this from
/// <c>Encoding.UTF8.GetString(body.Span)</c>, so a zero-length MQTT delivery
/// produces <c>""</c> — and a zero-length delivery is exactly the sort of
/// malformed message that gets rejected. Refusing it here would throw inside
/// the capture path, be swallowed by the surrounding handler as though the
/// database were down, and silently lose the dead letter for one of the most
/// likely rejection causes. The invariant would have suppressed the evidence it
/// was meant to protect.
/// </para>
///
/// <para>
/// What the type does add is distinctness: <c>Capture</c> took topic, payload
/// and error as three adjacent strings, and transposing any two compiled
/// cleanly.
/// </para>
/// </summary>
public sealed record RawPayload : StringValueObject
{
    private RawPayload(string value)
        : base(value)
    {
    }

    public static RawPayload From(string value)
    {
        Ensure.That(value, nameof(value)).IsNotNull();

        return new RawPayload(value);
    }
}
