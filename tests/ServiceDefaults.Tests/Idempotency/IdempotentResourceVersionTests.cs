using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.ServiceDefaults.Tests.Idempotency;

/// <summary>
/// Spec 454 (#3096). The idempotency machinery stores and returns the version an answer carried
/// and applies no policy to it (plan I2): a mismatch means nothing here, only to the replay that
/// opted in. The identifier-only <c>ExecuteAsync</c> and the unversioned completion are the
/// shape the other nine creates keep, so they are pinned beside the new one.
/// </summary>
public class IdempotentResourceVersionTests
{
    private static readonly IdempotencyScope Scope =
        IdempotencyScope.For(
            IdempotencyKey.From("key-454"),
            "POST /webhook-integrations/{name}/rotate",
            "admin@fab",
            Option<string>.Some("munich"),
            IdempotencyFingerprint.Of(new ProbeRequest("fixed")));

    private static readonly Guid Resource = Guid.Parse("0198f1c0-0000-7000-8000-000000000454");

    private sealed record ProbeRequest(string Value);

    [Fact]
    public async Task Work_that_created_a_resource_at_a_version_completes_the_key_with_that_version()
    {
        RecordingStore store = new();

        await Run(
            store,
            _ => Task.FromResult(IdempotentOutcome.CreatedAt(Resource, AggregateVersion.From(5), Results.Ok("done"))));

        store.CompletedWithVersion.ShouldBe((Resource, AggregateVersion.From(5)));
        store.CompletedWithoutVersion.ShouldBeNull("the version-bearing completion replaces the plain one, it does not follow it.");
    }

    [Fact]
    public async Task Work_that_created_a_resource_without_a_version_completes_the_key_without_one()
    {
        RecordingStore store = new();

        await Run(store, _ => Task.FromResult(IdempotentOutcome.Created(Resource, Results.Ok("done"))));

        store.CompletedWithoutVersion.ShouldBe(Resource);
        store.CompletedWithVersion.ShouldBeNull();
    }

    [Fact]
    public async Task A_replay_receives_the_version_the_key_was_completed_with()
    {
        RecordingStore store = new()
        {
            Next = IdempotencyReservation.CompletedWith(
                Resource, Option<AggregateVersion>.Some(AggregateVersion.From(7))),
        };

        var replayed = await ReplayAsync(store);

        replayed.ShouldBe(new IdempotentReplay(Resource, Option<AggregateVersion>.Some(AggregateVersion.From(7))));
    }

    [Fact]
    public async Task A_replay_of_a_key_completed_without_a_version_receives_none()
    {
        RecordingStore store = new() { Next = IdempotencyReservation.CompletedWith(Resource) };

        var replayed = await ReplayAsync(store);

        replayed.ShouldBe(new IdempotentReplay(Resource, Option<AggregateVersion>.None));
    }

    [Fact]
    public async Task The_identifier_only_overload_still_replays_by_identifier_whatever_version_is_stored()
    {
        RecordingStore store = new()
        {
            Next = IdempotencyReservation.CompletedWith(
                Resource, Option<AggregateVersion>.Some(AggregateVersion.From(7))),
        };

        var result = await IdempotentRequest.ExecuteAsync(
            new IdempotentExecution(Option<IdempotencyScope>.Some(Scope), store, TimeProvider.System),
            (CancellationToken _) => throw new InvalidOperationException("a completed key must not run the work."),
            (Guid identifier, CancellationToken _) => Task.FromResult<IResult>(TypedResults.Ok($"replayed {identifier}")),
            CancellationToken.None);

        result.ShouldBeOfType<Ok<string>>().Value.ShouldBe($"replayed {Resource}");
    }

    [Fact]
    public async Task A_replay_that_receives_the_version_does_not_run_the_work()
    {
        RecordingStore store = new()
        {
            Next = IdempotencyReservation.CompletedWith(
                Resource, Option<AggregateVersion>.Some(AggregateVersion.From(7))),
        };

        await ReplayAsync(store);

        store.WorkRuns.ShouldBe(0);
    }

    private static async Task<IdempotentReplay> ReplayAsync(RecordingStore store)
    {
        IdempotentReplay? received = null;

        await IdempotentRequest.ExecuteWithVersionAsync(
            new IdempotentExecution(Option<IdempotencyScope>.Some(Scope), store, TimeProvider.System),
            (CancellationToken token) => store.WorkAsync(token),
            (IdempotentReplay replayed, CancellationToken _) =>
            {
                received = replayed;
                return Task.FromResult<IResult>(TypedResults.Ok("replayed"));
            },
            CancellationToken.None);

        return received.ShouldNotBeNull("the completed key must reach the replay delegate.");
    }

    private static Task<IResult> Run(
        RecordingStore store, Func<CancellationToken, Task<IdempotentOutcome>> work) =>
        IdempotentRequest.ExecuteWithVersionAsync(
            new IdempotentExecution(Option<IdempotencyScope>.Some(Scope), store, TimeProvider.System),
            work,
            (IdempotentReplay _, CancellationToken _) => Task.FromResult<IResult>(TypedResults.Ok("replayed")),
            CancellationToken.None);

    private sealed class RecordingStore : IIdempotencyStore
    {
        public int WorkRuns { get; private set; }

        public Guid? CompletedWithoutVersion { get; private set; }

        public (Guid Resource, AggregateVersion Version)? CompletedWithVersion { get; private set; }

        public IdempotencyReservation Next { get; set; } =
            IdempotencyReservation.ReservedAs(new IdempotencyClaim(Scope, default));

        public Task<IdempotentOutcome> WorkAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkRuns++;

            return Task.FromResult(IdempotentOutcome.Created(Resource, Results.Ok("done")));
        }

        public Task<IdempotencyReservation> BeginAsync(IdempotencyScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(Next);

        public Task CompleteAsync(IdempotencyClaim claim, Guid resourceIdentifier, CancellationToken cancellationToken)
        {
            CompletedWithoutVersion = resourceIdentifier;

            return Task.CompletedTask;
        }

        public Task CompleteAsync(
            IdempotencyClaim claim,
            Guid resourceIdentifier,
            AggregateVersion version,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompletedWithVersion = (resourceIdentifier, version);

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(IdempotencyClaim claim, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
