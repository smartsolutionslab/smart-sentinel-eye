using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Infrastructure.Ingress;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Spec 020 T010 and T020. The loop is where the promise is kept or broken, and
/// none of this can be established by reading it.
/// </summary>
public class PersistenceLoopHostedServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-19T09:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Acknowledges_each_delivery_in_a_stored_batch()
    {
        RecordingCompletion first = new();
        RecordingCompletion second = new();
        Harness harness = new(Delivery("a", first), Delivery("b", second));

        await harness.RunUntilAsync(() => first.Stored == 1 && second.Stored == 1);

        first.Stored.ShouldBe(1);
        second.Stored.ShouldBe(1);
        first.Abandoned.ShouldBe(0);
    }

    /// <summary>
    /// FR-004. The whole point: an interruption costs time, not events. Nothing
    /// is acknowledged while the write is failing, so the sender keeps its copy.
    /// </summary>
    [Fact]
    public async Task Retries_a_failed_delivery_and_acknowledges_nothing_until_it_lands()
    {
        RecordingCompletion completion = new();
        Harness harness = new(Delivery("a", completion))
        {
            FailuresBeforeSuccess = 2,
            // The window is a wall clock (AdvancingClock), and this is the only
            // test that must finish its retries inside one. Nothing here asserts
            // abandonment, so the window is given enough room that load cannot
            // cause it - the 10 s deadline in RunUntilAsync stays the only
            // failure bound.
            Window = TimeSpan.FromSeconds(30),
        };

        await harness.RunUntilAsync(() => completion.Stored == 1);

        completion.Stored.ShouldBe(1);
        completion.Abandoned.ShouldBe(0);
        harness.Attempts.ShouldBe(3, "two failures then the write that succeeded");
    }

    /// <summary>
    /// FR-007/FR-008. QoS 1 redelivers forever, so "keep trying" needs a
    /// stopping rule — and the delivery must be recorded before it is released,
    /// or this is the original defect with a bound on it.
    /// </summary>
    [Fact]
    public async Task Records_and_releases_a_delivery_that_never_stores()
    {
        RecordingCompletion completion = new();
        Harness harness = new(Delivery("a", completion)) { FailuresBeforeSuccess = int.MaxValue };

        await harness.RunUntilAsync(() => completion.Abandoned == 1, TimeSpan.FromSeconds(30));

        completion.Abandoned.ShouldBe(1);
        completion.Stored.ShouldBe(0);
        harness.DeadLetters.ShouldHaveSingleItem()
            .Error.Value.ShouldContain("not storable after");
    }

    /// <summary>
    /// FR-009, and the reason phase 6 exists. Keeping an event until it is
    /// stored is exactly what turns one unstorable delivery into an endless
    /// retry — this is the assertion that it does not take the batch with it.
    /// Spec 018 fixed that defect; this is where it could come back.
    /// </summary>
    [Fact]
    public async Task One_delivery_that_never_stores_does_not_hold_up_the_others()
    {
        RecordingCompletion poison = new();
        RecordingCompletion healthy = new();
        Harness harness = new(Delivery("poison", poison), Delivery("healthy", healthy))
        {
            PoisonPayload = "poison",
            // Far longer than this test waits, so the healthy event can only be
            // stored by the loop moving past the failure - not by the failure
            // being abandoned out of the way.
            Window = TimeSpan.FromSeconds(30),
        };

        await harness.RunUntilAsync(() => healthy.Stored == 1, TimeSpan.FromSeconds(10));

        healthy.Stored.ShouldBe(1, "a good event waited for a bad one");
        poison.Stored.ShouldBe(0);
    }

    /// <summary>
    /// FR-009, the version that a single served batch cannot show. The first
    /// design retried the failing batch to exhaustion before reading the channel
    /// again, so an event arriving one second after a poisoned one waited out the
    /// whole retry window — five minutes, in production. That is the defect spec
    /// 018 fixed, wearing a bound.
    /// </summary>
    [Fact]
    public async Task An_event_arriving_behind_a_failing_one_does_not_wait_for_it()
    {
        RecordingCompletion poison = new();
        RecordingCompletion later = new();

        BoundedIngestChannel channel = new(capacity: 10);
        await channel.WriteAsync(Delivery("poison", poison), CancellationToken.None);

        Harness harness = new()
        {
            PoisonPayload = "poison",
            ChannelOverride = channel,
            // Far longer than this test waits, so the healthy event can only be
            // stored by the loop moving past the failure - not by the failure
            // being abandoned out of the way.
            Window = TimeSpan.FromSeconds(30),
        };

        await harness.RunUntilAsync(
            () => later.Stored == 1,
            TimeSpan.FromSeconds(5),
            onStarted: async () =>
            {
                // Arrives while the poisoned delivery is still being retried.
                await Task.Delay(100, CancellationToken.None);
                await channel.WriteAsync(Delivery("healthy", later), CancellationToken.None);
            });

        later.Stored.ShouldBe(1, "a good event waited behind a failing one");
        poison.Stored.ShouldBe(0);
    }

    /// <summary>
    /// FR-010. The requirement is not "batching exists somewhere" but that
    /// ingest is not a round trip per event, and the only way to see that from
    /// outside is to count the saves. Three deliveries, one save.
    ///
    /// <para>
    /// Worth its own case because the batch path fails <i>open</i>: if the batch
    /// handler cannot be resolved or throws, the loop quietly stores them one at
    /// a time and every other test here still passes. That is how a performance
    /// path gets switched off without anyone noticing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_batch_is_stored_in_one_save_not_one_per_event()
    {
        RecordingCompletion first = new();
        RecordingCompletion second = new();
        RecordingCompletion third = new();
        Harness harness = new(
            Delivery("a", first), Delivery("b", second), Delivery("c", third));

        await harness.RunUntilAsync(
            () => first.Stored == 1 && second.Stored == 1 && third.Stored == 1);

        harness.Attempts.ShouldBe(1, "three events cost three round trips");
    }

    /// <summary>
    /// FR-008, on the path that had no test. An envelope a domain rule refuses
    /// — a source whose clock is days ahead — is never storable, so it must be
    /// <b>recorded</b> and then released. Before this it was acknowledged into
    /// silence with nothing but a log line, which is the exact loss this
    /// feature exists to close, happening on its own fast path.
    /// </summary>
    [Fact]
    public async Task An_envelope_no_rule_will_accept_is_recorded_before_it_is_released()
    {
        RecordingCompletion completion = new();
        Harness harness = new(Skewed(completion));

        await harness.RunUntilAsync(() => completion.Abandoned == 1, TimeSpan.FromSeconds(10));

        completion.Abandoned.ShouldBe(1);
        completion.Stored.ShouldBe(0, "nothing was stored, so nothing may be reported as stored");
        harness.DeadLetters.ShouldHaveSingleItem();
    }

    /// <summary>
    /// Spec 213 US1-B (issue #2428), the site at <c>:194</c>. Today
    /// <c>RecordRejectionAsync</c> can only ever write the window sentence, so
    /// this is red: a skewed envelope refused by an otherwise-healthy batch
    /// must record the rule it broke, not a fabricated "not storable after"
    /// line — that sentence belongs to a delivery that actually retried, which
    /// this one never did.
    ///
    /// <para>
    /// <b>Load-bearing pair</b> with
    /// <see cref="A_delivery_that_only_fails_transiently_still_names_the_retry_window_it_exhausted"/>
    /// below (US1-D counterfactual): an implementation that replaced the window
    /// sentence everywhere would pass this test and fail that one; an
    /// implementation that changed nothing would pass that one and fail this
    /// one. Neither test is redundant with the other — do not delete either
    /// because it looks covered by its neighbour.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_batch_refusal_records_the_rule_it_broke_not_the_retry_window()
    {
        RecordingCompletion completion = new();
        Harness harness = new(Skewed(completion));

        await harness.RunUntilAsync(() => completion.Abandoned == 1, TimeSpan.FromSeconds(10));

        DeadLetter deadLetter = harness.DeadLetters.ShouldHaveSingleItem();
        deadLetter.Error.Value.ShouldContain("EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE");
        deadLetter.Error.Value.ShouldContain("more than 5 minutes in the future");
        deadLetter.Error.Value.ShouldNotContain("of retrying");
    }

    /// <summary>
    /// Spec 213 US1-D, the counterfactual for US1-B above and the site at
    /// <c>:215</c> — the one place the window sentence is actually true. A
    /// delivery whose save fails on every attempt genuinely exhausts the retry
    /// window, so its dead letter must keep naming that window. This must stay
    /// green through the whole feature: see the pairing note on
    /// <see cref="A_batch_refusal_records_the_rule_it_broke_not_the_retry_window"/>
    /// above for why it cannot be deleted as redundant with it.
    ///
    /// <para>
    /// Deliberately a separate delivery and a separate assertion from
    /// <see cref="Records_and_releases_a_delivery_that_never_stores"/> — that
    /// test is the standing, untouched check that the sentence did not move;
    /// this one additionally names the exact window, which is the stronger
    /// claim US1-D asks for.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_delivery_that_only_fails_transiently_still_names_the_retry_window_it_exhausted()
    {
        TimeSpan window = TimeSpan.FromMilliseconds(500);
        RecordingCompletion completion = new();
        Harness harness = new(Delivery("a", completion))
        {
            FailuresBeforeSuccess = int.MaxValue,
            Window = window,
        };

        await harness.RunUntilAsync(() => completion.Abandoned == 1, TimeSpan.FromSeconds(30));

        DeadLetter deadLetter = harness.DeadLetters.ShouldHaveSingleItem();
        deadLetter.Error.Value.ShouldContain("not storable after");
        deadLetter.Error.Value.ShouldContain(window.ToString());
    }

    /// <summary>
    /// Spec 213 US1-F. <c>RejectionReason.From</c> throws above 512 characters,
    /// and that throw lands inside <c>RecordRejectionAsync</c>'s own catch —
    /// which returns false and leaves the delivery on <c>carried</c> forever,
    /// since the next attempt composes the identical over-long string. <c>
    /// Because</c> truncates instead, so a composed reason is always
    /// representable.
    ///
    /// <para>
    /// Direct rather than through the harness: no <see cref="IngestEventError"/>
    /// variant in production code can produce a message long enough to reach
    /// the bound (both are fixed-width), and <c>StoreOneAsync</c> resolves the
    /// concrete, sealed <see cref="IngestEventCommandHandler"/> — not an
    /// interface — so nothing can be substituted to force one through the
    /// loop. <c>Because</c> is <c>internal</c> for exactly this: the assembly
    /// already grants this test project <c>InternalsVisibleTo</c>, so no new
    /// production-code seam is needed, only this access.
    /// </para>
    /// </summary>
    [Fact]
    public void A_composed_reason_longer_than_the_512_character_bound_is_truncated_not_thrown()
    {
        IngestEventError overlong = new OverlongTestReason();

        RejectionReason reason = PersistenceLoopHostedService.Because(overlong);

        reason.Value.Length.ShouldBeLessThanOrEqualTo(RejectionReason.MaximumLength);
        reason.Value.ShouldStartWith(overlong.Code);
    }

    private sealed record OverlongTestReason()
        : IngestEventError("TEST_OVERLONG_REASON", new string('x', RejectionReason.MaximumLength + 100), System.Net.HttpStatusCode.BadRequest);

    /// <summary>
    /// Spec 213 US1-C, the site at <c>:368</c>. A skewed envelope on its own
    /// would take the healthy-batch path (US1-B); riding alongside a poisoned
    /// delivery instead forces the whole cycle to singles, which is a
    /// different call site with its own fabrication to fix.
    ///
    /// <para>
    /// Reachable, not assumed: <c>Build</c> never calls <c>events.Add</c> for
    /// the skewed envelope (the future-skew rule throws before that), so the
    /// batch's pending set holds only the poisoned delivery. Its save throws,
    /// <c>TryStoreBatchAsync</c> returns <c>None</c>, and <c>RetryAsync</c>
    /// then runs over every arrived delivery — including the skewed one, whose
    /// own single store returns a typed failure without throwing. That lands it
    /// on the first-attempt rejection path, not the exhausted-retry path.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_skewed_envelope_in_a_batch_that_throws_is_rejected_on_its_first_single_attempt()
    {
        RecordingCompletion skewed = new();
        RecordingCompletion poison = new();
        Harness harness = new(Skewed(skewed), Delivery("poison", poison))
        {
            PoisonPayload = "poison",
            // Long enough that the poisoned delivery cannot exhaust and get
            // abandoned before the test observes the skewed one - the skewed
            // delivery is rejected on its first single attempt, the poisoned
            // one is merely still failing.
            Window = TimeSpan.FromSeconds(30),
        };

        await harness.RunUntilAsync(() => skewed.Abandoned == 1, TimeSpan.FromSeconds(10));

        DeadLetter deadLetter = harness.DeadLetters.ShouldHaveSingleItem();
        deadLetter.Error.Value.ShouldContain("EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE");
        deadLetter.Error.Value.ShouldNotContain("of retrying");
        skewed.Abandoned.ShouldBe(1);
        skewed.Stored.ShouldBe(0);
        poison.Abandoned.ShouldBe(0, "nothing here has had time to exhaust the retry window");
    }

    /// <summary>
    /// Spec 213 US1-E, the conflict scenario at <c>:366-368</c>: carrying a
    /// reason through the singles path must not tempt an implementation into
    /// dead-lettering <c>EventAlreadyIngested</c> — that branch is the
    /// idempotency rule working, and a row for it would be a new defect. Green
    /// today and must stay green.
    ///
    /// <para>
    /// Forced onto the singles path deliberately: the batch handler's own
    /// idempotency check (<c>seen</c>/<c>already</c> in
    /// <c>IngestEventBatchCommandHandler</c>) would otherwise absorb a
    /// redelivery before it ever reaches <c>StoreOneAsync</c>'s
    /// <c>EventAlreadyIngested</c> ternary, so the test would pass without ever
    /// exercising the branch this spec threads a reason past. A poisoned
    /// delivery riding alongside the redelivery makes that cycle's batch throw,
    /// so both fall to singles and the redelivery is the one that actually
    /// reaches the ternary. Uses <see cref="BoundedIngestChannel"/> and a
    /// mid-run write, the same pattern
    /// <see cref="An_event_arriving_behind_a_failing_one_does_not_wait_for_it"/>
    /// uses, because a single served batch cannot show two arrivals in
    /// different cycles.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_redelivery_of_an_already_stored_event_is_completed_as_stored_not_dead_lettered()
    {
        RecordingCompletion original = new();
        RecordingCompletion poison = new();
        RecordingCompletion redelivered = new();

        IngestDelivery first = Delivery("dup", original);

        BoundedIngestChannel channel = new(capacity: 10);
        await channel.WriteAsync(first, CancellationToken.None);

        Harness harness = new()
        {
            ChannelOverride = channel,
            PoisonPayload = "poison",
            Window = TimeSpan.FromSeconds(30),
        };

        await harness.RunUntilAsync(
            () => redelivered.Stored == 1,
            TimeSpan.FromSeconds(5),
            onStarted: async () =>
            {
                // Waits for "dup" to have landed via the batch path, so the
                // redelivery below finds it already in the repository.
                await Task.Delay(200, CancellationToken.None);
                await channel.WriteAsync(Delivery("poison", poison), CancellationToken.None);
                await channel.WriteAsync(
                    new IngestDelivery(first.Envelope, redelivered), CancellationToken.None);
            });

        redelivered.Stored.ShouldBe(1);
        harness.DeadLetters.ShouldBeEmpty();
    }

    /// <summary>
    /// The unguarded await that would stop the service. Acknowledging an MQTT
    /// delivery puts a PUBACK on the wire and throws on a dropped connection;
    /// an exception escaping the loop faults the BackgroundService, whose
    /// default behaviour stops the host — spec 018's defect, reached by a
    /// broker blip, which is precisely the scenario this feature is built for.
    /// </summary>
    [Fact]
    public async Task An_acknowledgement_that_throws_does_not_take_the_loop_down()
    {
        RecordingCompletion angry = new() { ThrowOnStored = true };
        RecordingCompletion next = new();
        Harness harness = new(Delivery("a", angry), Delivery("b", next));

        await harness.RunUntilAsync(() => next.Stored == 1, TimeSpan.FromSeconds(10));

        next.Stored.ShouldBe(1, "the loop stopped at the first failed acknowledgement");
    }

    private static IngestDelivery Skewed(IIngestCompletion completion) =>
        new(
            new EventEnvelope(
                EventIdentifier.New(),
                FabIdentifier.From("munich"),
                Source.Plc,
                DeviceIdentifier.From("station-4"),
                Kind.From("PlcCycleStart"),
                OccurredAt.From(Now.AddDays(30)),
                Payload.From("{\"marker\":\"skewed\"}")),
            completion);

    private static IngestDelivery Delivery(string marker, IIngestCompletion completion) =>
        new(
            new EventEnvelope(
                EventIdentifier.New(),
                FabIdentifier.From("munich"),
                Source.Plc,
                DeviceIdentifier.From("station-4"),
                Kind.From("PlcCycleStart"),
                OccurredAt.From(Now),
                Payload.From("{\"marker\":\"" + marker + "\"}")),
            completion);

    /// <summary>
    /// Runs the real loop over a channel that serves one batch, against a
    /// repository whose failures are scripted.
    /// </summary>
    private sealed class Harness(params IngestDelivery[] batch)
    {
        public int FailuresBeforeSuccess { get; init; }

        /// <summary>Marker whose delivery always fails, whatever the others do.</summary>
        public string? PoisonPayload { get; init; }

        /// <summary>Channel to drain, when the test needs to add to it mid-run.</summary>
        public IIngestChannel? ChannelOverride { get; init; }

        /// <summary>Retry window, when the default would let abandonment do the unblocking.</summary>
        public TimeSpan? Window { get; init; }

        public int Attempts => repository.Attempts;

        public IReadOnlyList<DeadLetter> DeadLetters => deadLetters.Captured;

        /// <summary>
        /// Short enough to keep the abandon case a fast test. The bound is a
        /// duration in production too — five minutes — so shortening it here
        /// exercises the same code rather than a test-only branch.
        ///
        /// <para>
        /// Only a test whose own assertion depends on abandonment actually
        /// happening should rely on this default. A test whose assertion must
        /// NOT be satisfiable by a delivery merely being abandoned sets
        /// <see cref="Window"/> instead - relying on the default there silently
        /// lets abandonment satisfy the assertion, which is how the head-of-line
        /// guard came to pass under the very defect it names (spec 194, issue
        /// #2293). Most tests in this file take the default legitimately,
        /// because the window is never on their path at all.
        /// </para>
        /// </summary>
        private static readonly TimeSpan RetryWindow = TimeSpan.FromMilliseconds(500);

        private readonly ScriptedEventRepository repository = new();
        private readonly RecordingDeadLetterRepository deadLetters = new();
        private readonly AdvancingClock clock = new();

        public async Task RunUntilAsync(
            Func<bool> condition, TimeSpan? timeout = null, Func<Task>? onStarted = null)
        {
            repository.FailuresBeforeSuccess = FailuresBeforeSuccess;
            repository.PoisonPayload = PoisonPayload;

            ServiceCollection services = new();
            services.AddSingleton<IEventRepository>(repository);
            services.AddSingleton<IDeadLetterRepository>(deadLetters);
            services.AddSingleton<IClock>(new FixedClock());
            services.AddLogging();
            services.AddScoped<IngestEventCommandHandler>();
            services.AddScoped<IngestEventBatchCommandHandler>();

            await using ServiceProvider provider = services.BuildServiceProvider();

            PersistenceLoopHostedService loop = new(
                ChannelOverride ?? new OneBatchChannel(batch),
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new IngestRetryOptions
                {
                    // Short enough that the abandon case is a fast test, rather
                    // than one that sits out the five-minute production window.
                    MaximumRetryWindow = Window ?? RetryWindow,
                    InitialBackoff = TimeSpan.FromMilliseconds(10),
                    MaximumBackoff = TimeSpan.FromMilliseconds(50),
                }),
                clock,
                NullLogger<PersistenceLoopHostedService>.Instance);

            using CancellationTokenSource cts = new(timeout ?? TimeSpan.FromSeconds(10));
            await loop.StartAsync(cts.Token);

            Task arrivals = onStarted is null ? Task.CompletedTask : onStarted();

            while (!condition() && !cts.IsCancellationRequested)
            {
                await Task.Delay(50, CancellationToken.None);
            }

            await arrivals;

            await loop.StopAsync(CancellationToken.None);
        }
    }

    private sealed class OneBatchChannel(IReadOnlyList<IngestDelivery> batch) : IIngestChannel
    {
        private bool served;

        public int CurrentDepth => 0;

        public IReadOnlyList<IngestDelivery> TakeAvailable(int maximum) => [];

        public ValueTask WriteAsync(IngestDelivery delivery, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public async Task<IReadOnlyList<IngestDelivery>> ReadBatchAsync(
            int maximum, CancellationToken cancellationToken)
        {
            if (served)
            {
                // Nothing more is coming; wait for the loop to be stopped.
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            served = true;
            return batch;
        }
    }

    private sealed class ScriptedEventRepository : IEventRepository
    {
        private readonly List<EventAggregate> stored = [];

        public int Attempts { get; private set; }

        public int FailuresBeforeSuccess { get; set; }

        public string? PoisonPayload { get; set; }

        private readonly List<EventAggregate> pending = [];

        public Task<Option<EventAggregate>> GetByIdentifierAsync(
            FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken) =>
            Task.FromResult(Option<EventAggregate>.None);

        public Task<bool> ExistsAsync(
            FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken) =>
            Task.FromResult(stored.Any(e => e.Fab == fab && e.Id == identifier));

        public Task<IReadOnlySet<EventIdentifier>> ExistingAsync(
            IReadOnlyCollection<(FabIdentifier Fab, EventIdentifier Identifier)> candidates,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<EventIdentifier>>(
                candidates
                    .Where(candidate => stored.Any(e => e.Fab == candidate.Fab && e.Id == candidate.Identifier))
                    .Select(candidate => candidate.Identifier)
                    .ToHashSet());

        public void Add(EventAggregate @event) => pending.Add(@event);

        /// <summary>
        /// One save covers everything added since the last one, and any poison
        /// among them fails all of it — which is what the real repository does
        /// and what the loop's fallback path exists to survive. Modelling a
        /// single pending event would let a poisoned delivery be stored simply
        /// because a healthy one was added after it.
        /// </summary>
        public Task SaveAsync(CancellationToken cancellationToken)
        {
            Attempts++;

            bool poisoned = PoisonPayload is not null
                && pending.Exists(@event =>
                    @event.Payload.Value.Contains(PoisonPayload, StringComparison.Ordinal));

            if (poisoned || Attempts <= FailuresBeforeSuccess)
            {
                pending.Clear();
                throw new InvalidOperationException("scripted persistence failure");
            }

            stored.AddRange(pending);
            pending.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDeadLetterRepository : IDeadLetterRepository
    {
        private readonly List<DeadLetter> captured = [];

        public IReadOnlyList<DeadLetter> Captured => captured;

        public void Add(DeadLetter deadLetter) => captured.Add(deadLetter);

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingCompletion : IIngestCompletion
    {
        public int Stored { get; private set; }

        public int Abandoned { get; private set; }

        /// <summary>Stands in for a broker connection that has dropped.</summary>
        public bool ThrowOnStored { get; init; }

        public Task StoredAsync(CancellationToken cancellationToken)
        {
            Stored++;
            if (ThrowOnStored)
            {
                throw new InvalidOperationException("the delivery is not connected");
            }

            return Task.CompletedTask;
        }

        public Task AbandonedAsync(CancellationToken cancellationToken)
        {
            Abandoned++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Real elapsed time from a fixed start. The retry bound is a duration, so a
    /// frozen clock would make it unreachable and the abandon test would hang
    /// rather than fail — which is how a bound that never fires gets shipped.
    /// </summary>
    private sealed class AdvancingClock : IClock
    {
        private readonly System.Diagnostics.Stopwatch elapsed =
            System.Diagnostics.Stopwatch.StartNew();

        public DateTimeOffset UtcNow => Now + elapsed.Elapsed;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
