using SmartSentinelEye.EventIngestion.Application.Ingress;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// An envelope the batch refused, paired with the typed reason it was
/// refused for.
///
/// <para>
/// The reason travels with the envelope rather than being logged and
/// dropped: the caller owes the refusal a dead letter before it releases
/// the sender's copy (spec 020 FR-008), and a dead letter that cannot say
/// why is the defect this type exists to close.
/// </para>
/// </summary>
public sealed record RefusedEnvelope(EventEnvelope Envelope, IngestEventError Reason);
