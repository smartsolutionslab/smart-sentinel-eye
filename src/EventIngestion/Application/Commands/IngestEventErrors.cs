using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

public abstract record IngestEventError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>
    /// The (fab, eventId) pair is already in the events table.
    /// MQTT redelivery on a flaky network is the common cause; the
    /// caller sees a 200 OK (idempotent) and no second
    /// FabEventIngestedV1 fires (spec 006 FR-002).
    /// </summary>
    public sealed record EventAlreadyIngested(Guid Identifier)
        : IngestEventError(
            "EVENT_ALREADY_INGESTED",
            $"Event {Identifier} has already been ingested for this fab.",
            HttpStatusCode.OK);

    /// <summary>
    /// The producer's clock is more than 5 minutes ahead of ours
    /// (spec 006 FR-014). Rejected so out-of-order downstream
    /// processing doesn't get fed nonsense.
    /// </summary>
    public sealed record OccurredAtTooFarInFuture(DateTimeOffset OccurredAt)
        : IngestEventError(
            "EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE",
            $"occurredAt {OccurredAt:O} is more than 5 minutes in the future; check the source's clock.",
            HttpStatusCode.BadRequest);
}
