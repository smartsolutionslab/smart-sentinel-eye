using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Phase 4a (spec 269 T003d.7). Green on arrival: <c>Because</c> already
/// truncates to <see cref="RejectionReason.MaximumLength"/> (spec 213, issue
/// #2428) — this pins that a maximum-length fab and kind in
/// <c>EventTypeNotRegistered</c>'s message still produces a valid
/// <see cref="RejectionReason"/> rather than throwing, so the MQTT path can
/// carry the refusal into a dead letter unmodified.
/// </summary>
public class EventTypeNotRegisteredDeadLetterReasonTests
{
    [Fact]
    public void The_refusal_fits_a_dead_letter_reason()
    {
        string maximumFab = "a" + new string('b', 31); // 32 chars, FabIdentifier.MaximumLength
        string maximumKind = "K" + new string('a', 127); // 128 chars, Kind.MaximumLength
        IngestEventError error = IngestEventFailures.EventTypeNotRegistered(maximumFab, "manual", maximumKind);

        RejectionReason reason = PersistenceLoopHostedService.Because(error);

        reason.Value.Length.ShouldBeLessThanOrEqualTo(RejectionReason.MaximumLength);
        reason.Value.ShouldStartWith("EVENT_TYPE_NOT_REGISTERED:");
    }
}
