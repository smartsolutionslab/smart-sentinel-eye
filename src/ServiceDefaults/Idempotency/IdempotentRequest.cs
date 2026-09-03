using Microsoft.AspNetCore.Http;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>What an idempotent operation produced: the identifier to remember, and the answer to send.</summary>
/// <param name="ResourceIdentifier">Recorded against the key, and all a replay needs to rebuild the answer.</param>
/// <param name="Response">What this first caller receives.</param>
public sealed record IdempotentOutcome(Guid ResourceIdentifier, IResult Response);

/// <summary>
/// The collaborators an idempotent execution needs. Bundled so
/// <see cref="IdempotentRequest.ExecuteAsync"/> keeps a readable parameter list
/// (ADR-0084).
/// </summary>
/// <param name="Scope">The caller's key with its endpoint and subject, or <c>None</c> to run unguarded.</param>
/// <param name="Store">The context's durable key record.</param>
/// <param name="Clock">Drives the in-progress wait; injected so tests need no real delay.</param>
public sealed record IdempotentExecution(
    Option<IdempotencyScope> Scope,
    IIdempotencyStore Store,
    TimeProvider Clock);

/// <summary>
/// Runs an operation at most once per idempotency key, replaying the original
/// answer for a repeat (ADR-0142).
///
/// <para>
/// The interesting case is the middle one. A retry exists <i>because</i> the
/// first attempt was slow, so it usually arrives while that attempt is still
/// running — and a store that only knows "seen" or "not seen" replays nothing in
/// exactly the window that produced the failure. This waits briefly for the
/// first attempt to land and replays its answer, and only refuses if it is still
/// unfinished.
/// </para>
/// </summary>
public static class IdempotentRequest
{
    /// <summary>How many times to re-check a reservation another attempt holds.</summary>
    private const int InProgressPolls = 10;

    /// <summary>Gap between those checks. Ten of these sits inside a 10 s attempt timeout.</summary>
    private static readonly TimeSpan InProgressPollInterval = TimeSpan.FromMilliseconds(500);

    public static async Task<IResult> ExecuteAsync(
        IdempotentExecution execution,
        Func<CancellationToken, Task<IdempotentOutcome>> work,
        Func<Guid, CancellationToken, Task<IResult>> replay,
        CancellationToken cancellationToken)
    {
        Ensure.That(execution).IsNotNull();
        Ensure.That(work).IsNotNull();
        Ensure.That(replay).IsNotNull();

        // No key means the caller did not ask for the guarantee, so nothing here
        // applies and the endpoint keeps the behaviour it has always had.
        if (!execution.Scope.HasValue)
        {
            return (await work(cancellationToken)).Response;
        }

        IdempotencyScope scope = execution.Scope.Value;

        IdempotencyReservation reservation = await ClaimAsync(execution, scope, cancellationToken);

        if (reservation.ResourceIdentifier.HasValue)
        {
            return await replay(reservation.ResourceIdentifier.Value, cancellationToken);
        }

        if (reservation.Outcome == IdempotencyOutcome.InProgress)
        {
            return Results.Problem(
                title: IdempotencyHeaders.InProgressErrorCode,
                detail: "An earlier request with this Idempotency-Key is still running. Retry shortly.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return await RunAndRecordAsync(execution, scope, work, cancellationToken);
    }

    /// <summary>
    /// Claims the key, waiting out an unfinished attempt rather than refusing it
    /// immediately.
    /// </summary>
    private static async Task<IdempotencyReservation> ClaimAsync(
        IdempotentExecution execution, IdempotencyScope scope, CancellationToken cancellationToken)
    {
        IdempotencyReservation reservation = await execution.Store.BeginAsync(scope, cancellationToken);

        for (int poll = 0; poll < InProgressPolls && reservation.Outcome == IdempotencyOutcome.InProgress; poll++)
        {
            await Task.Delay(InProgressPollInterval, execution.Clock, cancellationToken);
            reservation = await execution.Store.BeginAsync(scope, cancellationToken);
        }

        return reservation;
    }

    /// <summary>
    /// Does the work and records its identifier, releasing the reservation on any
    /// failure so the key does not stay wedged as in-progress.
    /// </summary>
    private static async Task<IResult> RunAndRecordAsync(
        IdempotentExecution execution,
        IdempotencyScope scope,
        Func<CancellationToken, Task<IdempotentOutcome>> work,
        CancellationToken cancellationToken)
    {
        IdempotentOutcome outcome;
        try
        {
            outcome = await work(cancellationToken);
        }
        catch
        {
            // Released with CancellationToken.None on purpose: the usual reason
            // this path runs is that the caller gave up, and a release that
            // inherits the cancelled token would not run at all — leaving the key
            // reserved forever by the very request that abandoned it.
            await execution.Store.ReleaseAsync(scope, CancellationToken.None);

            throw;
        }

        await execution.Store.CompleteAsync(scope, outcome.ResourceIdentifier, CancellationToken.None);

        return outcome.Response;
    }
}
