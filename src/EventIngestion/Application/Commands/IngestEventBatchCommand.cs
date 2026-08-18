using SmartSentinelEye.EventIngestion.Application.Ingress;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// Store several envelopes in one pass (spec 020 FR-010).
///
/// <para>
/// A separate command rather than a loop over <see cref="IngestEventCommand"/>
/// because the saving is the point: one existence query and one insert for the
/// whole batch instead of two round trips per event. FR-010 forbids ingest
/// becoming a round trip per event, and the single-event handler is exactly
/// that when it is called in a loop.
/// </para>
///
/// <para>
/// It is the <b>fast path only</b>. It is all-or-nothing by construction — one
/// unstorable envelope fails the whole insert — so its caller must fall back to
/// the single-event handler when it fails, or one bad row would cost the other
/// 199 and FR-009 with them.
/// </para>
/// </summary>
public sealed record IngestEventBatchCommand(IReadOnlyList<EventEnvelope> Envelopes);
