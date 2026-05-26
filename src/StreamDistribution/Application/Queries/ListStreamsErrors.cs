using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Queries;

public abstract record ListStreamsError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidBatchSize(int Requested, int Maximum)
        : ListStreamsError(
            "STREAM_INVALID_BATCH_SIZE",
            $"Requested {Requested} camera identifiers; maximum batch size is {Maximum}.",
            HttpStatusCode.BadRequest);
}
