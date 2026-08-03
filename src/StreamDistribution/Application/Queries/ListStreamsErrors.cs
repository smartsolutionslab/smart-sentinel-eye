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

/// <summary>
/// Builds a <see cref="ListStreamsError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class ListStreamsFailures
{
    public static ListStreamsError InvalidBatchSize(int requested, int maximum) =>
        new ListStreamsError.InvalidBatchSize(requested, maximum);
}
