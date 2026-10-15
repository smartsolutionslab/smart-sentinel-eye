using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Api;

/// <summary>Stores an ingested event or refuses it, for the write routes of <see cref="EventsEndpoints"/>.</summary>
internal static class EventIngestStore
{
    /// <summary>
    /// Stores the event, then answers (spec 020 FR-001). Replaces the enqueue
    /// that answered 202 the moment the envelope was buffered.
    ///
    /// <para>
    /// <b>201, not 202.</b> 202 means "accepted for processing, outcome
    /// unknown", which is exactly the promise this feature removes — and the
    /// promise that used to be broken silently whenever the write failed
    /// afterwards. Once the row is committed before the response, 201 with a
    /// <c>Location</c> is the truthful answer, and the location resolves.
    /// </para>
    ///
    /// <para>
    /// <b>429 when the limiter is saturated</b>, not when a channel is full.
    /// These writes no longer use the channel, so without this the endpoint
    /// would quietly become "queue indefinitely and time out" — a worse answer
    /// to overload than a fast refusal, arrived at by omission (FR-013).
    /// </para>
    /// </summary>
    /// <summary>
    /// Yields the stored event's identifier, or the problem to answer with.
    ///
    /// <para>
    /// Returns the identifier rather than a finished <c>IResult</c> so the manual
    /// path can hand it to <c>IdempotentRequest.ExecuteCreateAsync</c>, which
    /// needs to record what was created (ADR-0142). The webhook path maps it
    /// straight back to a 201 and is otherwise unchanged.
    /// </para>
    /// </summary>
    internal static async Task<Result<Guid, IResult>> StoreOrRefuseAsync(
        IngestWriteLimiter limiter,
        IServiceScopeFactory scopeFactory,
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        using var lease = limiter.TryAcquire();
        if (!lease.Acquired)
        {
            return Result<Guid, IResult>.Failure(Results.Problem(
                title: "EVENT_INGEST_BACKPRESSURE",
                detail: "Too many events are being stored at once; please retry.",
                statusCode: StatusCodes.Status429TooManyRequests));
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var handler =
            scope.ServiceProvider.GetRequiredService<IngestEventCommandHandler>();

        try
        {
            var result =
                await handler.HandleAsync(new IngestEventCommand(envelope), cancellationToken);

            // Spec 317 (#2325), A2: a held event is a 2xx — accepted, not an
            // error — so it takes the same "answered without a Location"
            // branch as a problem rather than ToProblem()'s ProblemDetails
            // body, which exists for actual failures.
            return result.Match(
                onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
                onFailure: error => Result<Guid, IResult>.Failure(
                    error is IngestEventError.EventTypeHeld ? Results.Accepted() : error.ToProblem()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The caller is told, which is the whole point: a 202 followed by
            // silence is what this replaces. Nothing has been stored and
            // nothing has been buffered, so retrying is safe and is the
            // caller's to decide.
            return Result<Guid, IResult>.Failure(Results.Problem(
                title: "EVENT_NOT_STORED",
                detail: "The event could not be stored and has not been accepted. Retry.",
                statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }
}
