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
/// It does refuse emptiness, which is a change: <c>Capture</c> guarded this with
/// <c>IsNotNull()</c> alone, so an empty payload was capturable. Such a row
/// asserts that something was rejected while carrying nothing to inspect.
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
        Ensure.That(value, nameof(value)).IsNotNullOrWhiteSpace();

        return new RawPayload(value);
    }
}
