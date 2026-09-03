using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Tests.Idempotency;

/// <summary>
/// ADR-0142's execution rules. The cases that matter are the ones a simpler
/// design gets wrong: a retry arriving while the first attempt is still running,
/// and a failed attempt that must not wedge its key.
/// </summary>
public class IdempotentRequestTests
{
    private static readonly IdempotencyScope Scope =
        IdempotencyScope.For(IdempotencyKey.From("key-1"), "POST /devices/register", "admin@fab");

    private static readonly Guid Created = Guid.Parse("0198f1c0-0000-7000-8000-000000000001");

    [Fact]
    public async Task Without_a_key_the_work_runs_and_the_store_is_never_touched()
    {
        RecordingStore store = new();

        IResult result = await Run(Option<IdempotencyScope>.None, store, TimeProvider.System);

        ShouldBeOk(result, "done");
        store.Begins.ShouldBe(0, "an endpoint without a key must behave exactly as it did before ADR-0142.");
    }

    [Fact]
    public async Task A_first_arrival_does_the_work_and_records_its_identifier()
    {
        RecordingStore store = new();

        IResult result = await Run(store);

        ShouldBeOk(result, "done");
        store.Completed.ShouldBe(Created);
    }

    [Fact]
    public async Task A_repeat_of_a_completed_key_replays_rather_than_repeating_the_work()
    {
        RecordingStore store = new() { Next = IdempotencyReservation.CompletedWith(Created) };

        IResult result = await Run(store);

        ShouldBeOk(result, $"replayed {Created}");
        store.WorkRuns.ShouldBe(0, "the whole point is that the operation is applied once.");
    }

    /// <summary>
    /// The case the observed failure was: the retry arrives because the first
    /// attempt is slow, so it lands while that attempt is still running. A store
    /// that only knew "seen or not" would replay nothing here.
    /// </summary>
    [Fact]
    public async Task A_retry_that_arrives_mid_flight_waits_for_the_first_attempt_and_replays_it()
    {
        RecordingStore store = new()
        {
            Next = IdempotencyReservation.InProgress,
            LandsAfter = 3,
        };

        IResult result = await Run(store, new InstantClock());

        ShouldBeOk(result, $"replayed {Created}");
        store.Begins.ShouldBe(4, "three in-progress reads, then the one that saw it land.");
    }

    [Fact]
    public async Task A_first_attempt_that_never_lands_is_refused_rather_than_waited_on_forever()
    {
        RecordingStore store = new() { Next = IdempotencyReservation.InProgress };

        IResult result = await Run(store, new InstantClock());

        ProblemHttpResult problem = result.ShouldBeOfType<ProblemHttpResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Title.ShouldBe(IdempotencyHeaders.InProgressErrorCode);
    }

    /// <summary>
    /// Without the release, a request that threw would leave its key reserved
    /// forever — and every retry, which is the thing this mechanism exists to
    /// serve, would be refused for as long as the row survived.
    /// </summary>
    [Fact]
    public async Task A_failed_attempt_releases_its_key_so_a_retry_can_claim_it()
    {
        RecordingStore store = new() { Throws = new InvalidOperationException("Keycloak said no") };

        await Should.ThrowAsync<InvalidOperationException>(() => Run(store));

        store.Released.ShouldBe(1);
        store.Completed.ShouldBeNull();
    }

    /// <summary>
    /// The release must survive the cancellation that usually caused it — the
    /// caller giving up is the ordinary reason this path runs, and a release that
    /// inherited the cancelled token would not run at all.
    /// </summary>
    [Fact]
    public async Task A_cancelled_attempt_still_releases_its_key()
    {
        RecordingStore store = new() { Throws = new OperationCanceledException() };

        await Should.ThrowAsync<OperationCanceledException>(() => Run(store));

        store.Released.ShouldBe(1);
    }

    /// <summary>
    /// A refusal is a successful call that created nothing. Completing its key
    /// would make the next retry replay a resource that never existed; releasing
    /// it lets a caller fix the request and retry with the same key.
    /// </summary>
    [Fact]
    public async Task An_operation_that_creates_nothing_releases_its_key_rather_than_completing_it()
    {
        RecordingStore store = new() { CreatesNothing = true };

        IResult result = await Run(store);

        ShouldBeOk(result, "refused");
        store.Completed.ShouldBeNull();
        store.Released.ShouldBe(1);
    }

    private static Task<IResult> Run(RecordingStore store) => Run(store, TimeProvider.System);

    private static Task<IResult> Run(RecordingStore store, TimeProvider clock) =>
        Run(Option<IdempotencyScope>.Some(Scope), store, clock);

    private static Task<IResult> Run(
        Option<IdempotencyScope> scope, RecordingStore store, TimeProvider clock) =>
        IdempotentRequest.ExecuteAsync(
            new IdempotentExecution(scope, store, clock),
            store.WorkAsync,
            (identifier, _) => Task.FromResult<IResult>(TypedResults.Ok($"replayed {identifier}")),
            CancellationToken.None);

    /// <summary>
    /// Read off the typed result rather than executed against an
    /// <c>HttpContext</c>. Executing needs a DI container for JSON options and
    /// problem details, which is a lot of scaffolding to assert a string these
    /// tests already know.
    /// </summary>
    private static void ShouldBeOk(IResult result, string expected) =>
        result.ShouldBeOfType<Ok<string>>().Value.ShouldBe(expected);

    /// <summary>Every delay completes at once, so a five-second wait costs nothing.</summary>
    private sealed class InstantClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            base.CreateTimer(callback, state, TimeSpan.Zero, period);
    }

    private sealed class RecordingStore : IIdempotencyStore
    {
        public int Begins { get; private set; }

        public int Released { get; private set; }

        public int WorkRuns { get; private set; }

        public Guid? Completed { get; private set; }

        public IdempotencyReservation Next { get; set; } = IdempotencyReservation.Reserved;

        public Exception? Throws { get; set; }

        /// <summary>Answer the call successfully, having created nothing.</summary>
        public bool CreatesNothing { get; set; }

        /// <summary>After this many in-progress reads, report the key as landed.</summary>
        public int LandsAfter { get; set; } = int.MaxValue;

        public Task<IdempotentOutcome> WorkAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkRuns++;

            if (Throws is not null)
            {
                return Task.FromException<IdempotentOutcome>(Throws);
            }

            return Task.FromResult(
                CreatesNothing
                    ? IdempotentOutcome.NothingCreated(TypedResults.Ok("refused"))
                    : IdempotentOutcome.Created(Created, TypedResults.Ok("done")));
        }

        public Task<IdempotencyReservation> BeginAsync(IdempotencyScope scope, CancellationToken cancellationToken)
        {
            Begins++;

            return Task.FromResult(
                Next.Outcome == IdempotencyOutcome.InProgress && Begins > LandsAfter
                    ? IdempotencyReservation.CompletedWith(Created)
                    : Next);
        }

        public Task CompleteAsync(IdempotencyScope scope, Guid resourceIdentifier, CancellationToken cancellationToken)
        {
            Completed = resourceIdentifier;

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(IdempotencyScope scope, CancellationToken cancellationToken)
        {
            Released++;

            return Task.CompletedTask;
        }
    }
}
