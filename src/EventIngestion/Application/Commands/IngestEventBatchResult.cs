namespace SmartSentinelEye.EventIngestion.Application.Commands;

/// <summary>
/// What a batch write left behind: the envelopes a domain rule refused and
/// which therefore were not stored (spec 020 FR-008).
///
/// <para>
/// Everything not listed here is in the database — either written by this
/// batch, or already present from a redelivery, which for the sender is the
/// same answer. The refusals are the ones the caller still owes something to:
/// they must be recorded before the sender's copy is released, because they
/// will be refused identically for ever and acknowledging them into silence is
/// the loss this feature exists to close — and each one now carries
/// <i>why</i>, so the dead letter it feeds can say more than that one was
/// written (spec 213).
/// </para>
/// </summary>
public sealed record IngestEventBatchResult(IReadOnlyList<RefusedEnvelope> Refused);
