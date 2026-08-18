using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        Harness harness = new(Delivery("a", completion)) { FailuresBeforeSuccess = 2 };

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
            .Error.ShouldContain("not storable after");
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

        private EventAggregate? pending;

        public Task<Option<EventAggregate>> GetByIdentifierAsync(
            FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken) =>
            Task.FromResult(Option<EventAggregate>.None);

        public Task<bool> ExistsAsync(
            FabIdentifier fab, EventIdentifier identifier, CancellationToken cancellationToken) =>
            Task.FromResult(stored.Any(e => e.Fab == fab && e.Id == identifier));

        public void Add(EventAggregate @event) => pending = @event;

        public Task SaveAsync(CancellationToken cancellationToken)
        {
            Attempts++;

            bool poisoned = PoisonPayload is not null
                && pending is not null
                && pending.Payload.Value.Contains(PoisonPayload, StringComparison.Ordinal);

            if (poisoned || Attempts <= FailuresBeforeSuccess)
            {
                throw new InvalidOperationException("scripted persistence failure");
            }

            if (pending is not null)
            {
                stored.Add(pending);
                pending = null;
            }

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

        public Task StoredAsync(CancellationToken cancellationToken)
        {
            Stored++;
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
