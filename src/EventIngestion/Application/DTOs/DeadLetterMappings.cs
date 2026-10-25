using SmartSentinelEye.EventIngestion.Domain.DeadLetter;

namespace SmartSentinelEye.EventIngestion.Application.DTOs;

internal static class DeadLetterMappings
{
    public static DeadLetterDto ToDto(this DeadLetter deadLetter) =>
        new(
            deadLetter.Id.Value,
            deadLetter.Topic.Value,
            deadLetter.RawPayload.Value,
            deadLetter.Error.Value,
            deadLetter.RejectedAt,
            deadLetter.Fab?.Value,
            deadLetter.Reason.Value,
            deadLetter.Kind?.Value,
            deadLetter.State.Value);
}
