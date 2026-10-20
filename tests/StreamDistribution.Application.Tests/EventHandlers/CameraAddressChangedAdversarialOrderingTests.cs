using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.EventHandlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.EventHandlers;

/// <summary>
/// Spec 385 (#2991), adversarial. The real event handler over the real command
/// handler, only the ports faked, driven through schedules the happy-path
/// tests do not reach: every arrival order with gateway failures and
/// redeliveries, equal instants, an instant stamped in the far future, and
/// hostile URLs on the ignored-change log line.
/// </summary>
public class CameraAddressChangedAdversarialOrderingTests
{
    private const string OriginalUrl = "rtsp://camera-sim:8554/original";

    private const string AddressA = "rtsp://camera-sim:8554/a";

    private const string AddressB = "rtsp://camera-sim:8554/b";

    private const string AddressC = "rtsp://camera-sim:8554/c";

    private const string Marker = "SECRET-MARKER";

    private const string CameraText = "0197a1b2-0000-7000-8000-0000000000e3";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));

    private static readonly Guid CameraGuid = Guid.Parse(CameraText, CultureInfo.InvariantCulture);

    private static readonly CameraIdentifier Camera = CameraIdentifier.From(CameraGuid);

    private static readonly EventMetadata Metadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000ab", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-10-14T08:00:00Z", CultureInfo.InvariantCulture),
        "munich",
        null);

    private static readonly (string Url, DateTimeOffset ChangedAt)[] Changes =
    [
        (AddressA, Moment.AddSeconds(1)),
        (AddressB, Moment.AddSeconds(2)),
        (AddressC, Moment.AddSeconds(3)),
    ];

    private static readonly int[][] ArrivalOrders =
    [
        [0, 1, 2],
        [0, 2, 1],
        [1, 0, 2],
        [1, 2, 0],
        [2, 0, 1],
        [2, 1, 0],
    ];

    private enum RetryPlacement
    {
        BackOfTheQueue,
        FrontOfTheQueue,
    }

    private sealed class Harness
    {
        public Harness()
        {
            // An hour after every stamp these tests send: each change is in the
            // receiver's past, so nothing here depends on how a stamp ahead of
            // the receiving clock is treated.
            var clock = new FixedClock(Moment.AddHours(1));
            var commandHandler = new RepointStreamCommandHandler(
                Repository,
                Gateway,
                clock,
                Logger);
            Handler = new CameraAddressChangedIntegrationEventHandler(
                commandHandler,
                EventLogger);
            Provisioner = new CameraRegisteredIntegrationEventHandler(
                new ProvisionStreamCommandHandler(
                    Repository,
                    Gateway,
                    clock,
                    NullLogger<ProvisionStreamCommandHandler>.Instance),
                NullLogger<CameraRegisteredIntegrationEventHandler>.Instance);
        }

        public InMemoryStreamRepository Repository { get; } = new();

        public FakeRtspGateway Gateway { get; } = new();

        public CapturingLogger<RepointStreamCommandHandler> Logger { get; } = new();

        public CapturingLogger<CameraAddressChangedIntegrationEventHandler> EventLogger { get; } = new();

        public IEnumerable<string> AllMessages =>
            Logger.Entries.Concat(EventLogger.Entries).Select(entry => entry.Message);

        public CameraAddressChangedIntegrationEventHandler Handler { get; }

        public CameraRegisteredIntegrationEventHandler Provisioner { get; }

        public Domain.Stream.Stream Stream => Repository.Streams.Single();

        public async Task SeedAsync()
        {
            var stream = new StreamBuilder()
                .ForCamera(Camera)
                .WithSourceUrl(StreamSourceUrl.From(OriginalUrl))
                .ProvisionedBy(AnAdmin)
                .At(Moment)
                .Build();

            Repository.Add(stream);
            await Repository.SaveAsync(CancellationToken.None);
        }

        public Task HandleAsync(
            string url,
            DateTimeOffset changedAt) =>
            Handler.Handle(
                new CameraAddressChangedV1(
                    Camera: CameraGuid,
                    Fab: "munich",
                    PreviousUrl: OriginalUrl,
                    Url: url,
                    ChangedAt: changedAt,
                    ChangedBy: AnAdmin.Value,
                    Metadata: Metadata),
                CancellationToken.None);

        public Task RegisterAsync(string url) =>
            Provisioner.Handle(
                new CameraRegisteredV1(
                    Camera: CameraGuid,
                    Name: "Line-1",
                    Url: url,
                    RegisteredAt: Moment,
                    RegisteredBy: AnAdmin.Value,
                    Metadata: Metadata),
                CancellationToken.None);

        public async Task<List<(LogLevel Level, string Message, Exception? Exception)>> LoggedWhileAsync(
            Func<Task> action)
        {
            var before = Logger.Entries.Count;
            await action();
            return [.. Logger.Entries.Skip(before)];
        }
    }

    private sealed record Schedule(
        int[] Order,
        int[] Failures,
        int[] Redeliveries,
        RetryPlacement Placement)
    {
        public override string ToString() =>
            $"order [{string.Join(",", Order.Select(Name))}] failures [{string.Join(",", Failures)}] "
            + $"redeliveries [{string.Join(",", Redeliveries)}] retries to {Placement}";

        private static string Name(int index) => ((char)('A' + index)).ToString();
    }

    private static IEnumerable<int[]> EveryCountTriple()
    {
        for (var first = 0; first <= 2; first++)
        {
            for (var second = 0; second <= 2; second++)
            {
                for (var third = 0; third <= 2; third++)
                {
                    yield return [first, second, third];
                }
            }
        }
    }

    private static IEnumerable<Schedule> EverySchedule() =>
        from order in ArrivalOrders
        from failures in EveryCountTriple()
        from redeliveries in EveryCountTriple()
        from placement in new[] { RetryPlacement.BackOfTheQueue, RetryPlacement.FrontOfTheQueue }
        select new Schedule(order, failures, redeliveries, placement);

    private static async Task<IReadOnlyList<string>> ViolationsOfAsync(Schedule schedule)
    {
        var (order, failures, redeliveries, placement) = schedule;

        var harness = new Harness();
        await harness.SeedAsync();

        var failuresLeft = failures.ToArray();
        var redeliveriesLeft = redeliveries.ToArray();
        List<string> violations = [];
        List<DateTimeOffset> attemptedInstants = [];

        harness.Gateway.OnRepointPath = (_, url) =>
        {
            var index = Array.FindIndex(Changes, change => change.Url == url);
            var changedAt = Changes[index].ChangedAt;
            attemptedInstants.Add(changedAt);

            if (harness.Stream.SourceChangedAt != SourceChangedAt.From(changedAt))
            {
                violations.Add(
                    $"gateway told {url} while the stream records {harness.Stream.SourceChangedAt}");
            }

            if (failuresLeft[index] > 0)
            {
                failuresLeft[index]--;
                throw new HttpRequestException("sfu unreachable");
            }
        };

        LinkedList<int> queue = new(order);

        while (queue.Count > 0)
        {
            var index = queue.First!.Value;
            queue.RemoveFirst();

            var savesBefore = harness.Repository.SaveCallCount;
            var attemptsBefore = attemptedInstants.Count;

            try
            {
                await harness.HandleAsync(Changes[index].Url, Changes[index].ChangedAt);

                if (redeliveriesLeft[index] > 0)
                {
                    redeliveriesLeft[index]--;
                    queue.AddLast(index);
                }
            }
            catch (InvalidOperationException)
            {
                if (placement == RetryPlacement.BackOfTheQueue)
                {
                    queue.AddLast(index);
                }
                else
                {
                    queue.AddFirst(index);
                }
            }

            var saves = harness.Repository.SaveCallCount - savesBefore;
            var attempts = attemptedInstants.Count - attemptsBefore;

            if (saves != attempts)
            {
                violations.Add($"a delivery of {Changes[index].Url} saved {saves} time(s) for {attempts} gateway call(s)");
            }
        }

        for (var attempt = 1; attempt < attemptedInstants.Count; attempt++)
        {
            if (attemptedInstants[attempt] < attemptedInstants[attempt - 1])
            {
                violations.Add($"gateway call {attempt} went back from {attemptedInstants[attempt - 1]:O} to {attemptedInstants[attempt]:O}");
            }
        }

        if (harness.Stream.SourceUrl.Value != AddressC)
        {
            violations.Add($"the stream ended on {harness.Stream.SourceUrl.Value}");
        }

        if (harness.Gateway.RepointCalls.Count == 0 || harness.Gateway.RepointCalls[^1].Source != AddressC)
        {
            violations.Add("the last successful gateway call was not the newest change");
        }

        return violations;
    }

    [Fact]
    public async Task Every_arrival_order_with_failures_and_redeliveries_ends_the_stream_and_the_sfu_on_the_newest_change()
    {
        List<string> broken = [];
        var schedules = 0;

        foreach (var schedule in EverySchedule())
        {
            schedules++;
            var violations = await ViolationsOfAsync(schedule);

            if (violations.Count > 0)
            {
                broken.Add($"{schedule}: {string.Join("; ", violations)}");
            }
        }

        schedules.ShouldBe(6 * 27 * 27 * 2);
        broken.ShouldBeEmpty(string.Join(Environment.NewLine, broken.Take(10)));
    }

    [Fact]
    public async Task Two_changes_stamped_with_the_same_instant_end_on_whichever_was_handled_last_even_a_retry_of_the_first()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        var failed = false;
        harness.Gateway.OnRepointPath = (_, url) =>
        {
            if (url == AddressA && !failed)
            {
                failed = true;
                throw new HttpRequestException("sfu unreachable");
            }
        };

        await Should.ThrowAsync<InvalidOperationException>(() => harness.HandleAsync(AddressA, Moment));
        await harness.HandleAsync(AddressB, Moment);
        await harness.HandleAsync(AddressA, Moment);

        harness.Stream.SourceUrl.Value.ShouldBe(AddressA);
        harness.Gateway.RepointCalls.Select(call => call.Source).ShouldBe([AddressB, AddressA]);
    }

    [Fact]
    public async Task A_redelivered_registration_neither_resets_the_recorded_instant_nor_reverts_the_address()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        var recorded = SourceChangedAt.From(Moment.AddSeconds(2));

        await harness.HandleAsync(AddressA, recorded.Value);
        await harness.RegisterAsync(OriginalUrl);
        await harness.HandleAsync(AddressB, Moment.AddSeconds(1));

        harness.Repository.Streams.Count.ShouldBe(1);
        harness.Stream.SourceChangedAt.ShouldBe(recorded);
        harness.Stream.SourceUrl.Value.ShouldBe(AddressA);
        harness.Gateway.AddCalls.Select(call => call.Source).ShouldBe([AddressA]);
        harness.Gateway.RepointCalls.Select(call => call.Source).ShouldBe([AddressA]);
    }

    [Fact]
    public async Task A_freshly_registered_stream_takes_its_first_change_whatever_its_instant()
    {
        var harness = new Harness();
        await harness.RegisterAsync(OriginalUrl);

        harness.Stream.SourceChangedAt.ShouldBeNull();

        await harness.HandleAsync(AddressA, Moment.AddYears(-30));

        harness.Stream.SourceUrl.Value.ShouldBe(AddressA);
        harness.Stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(Moment.AddYears(-30)));
        harness.Gateway.RepointCalls.Select(call => call.Source).ShouldBe([AddressA]);
    }

    [Fact]
    public async Task A_retired_stream_is_skipped_as_retired_for_newer_and_older_changes_alike()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        await harness.HandleAsync(AddressA, Moment.AddSeconds(2));
        harness.Stream.Retire(new FixedClock(Moment));
        await harness.Repository.SaveAsync(CancellationToken.None);
        var savesBefore = harness.Repository.SaveCallCount;
        var entriesBefore = harness.Logger.Entries.Count;

        await harness.HandleAsync(AddressB, Moment.AddSeconds(3));
        await harness.HandleAsync(AddressC, Moment.AddSeconds(1));

        harness.Stream.SourceUrl.Value.ShouldBe(AddressA);
        harness.Stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(Moment.AddSeconds(2)));
        harness.Repository.SaveCallCount.ShouldBe(savesBefore);
        harness.Gateway.RepointCalls.Select(call => call.Source).ShouldBe([AddressA]);
        harness.Logger.Entries.Skip(entriesBefore)
            .Select(entry => entry.Message)
            .ShouldAllBe(message => message.Contains("its stream is retired", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Provisioning")]
    [InlineData("Healthy")]
    [InlineData("Degraded")]
    [InlineData("Offline")]
    public async Task A_live_stream_in_any_state_is_ordered_the_same_and_keeps_its_state(string state)
    {
        var harness = new Harness();
        await harness.SeedAsync();
        var clock = new FixedClock(Moment);
        var error = StreamError.From("no frames");
        switch (state)
        {
            case "Healthy":
                harness.Stream.ReportHealthy(TranscodeMode.Passthrough, clock);
                break;
            case "Degraded":
                harness.Stream.ReportDegraded(error, clock);
                break;
            case "Offline":
                harness.Stream.ReportDegraded(error, clock);
                harness.Stream.ReportOffline(error, clock);
                break;
        }

        await harness.HandleAsync(AddressB, Moment.AddSeconds(2));
        await harness.HandleAsync(AddressA, Moment.AddSeconds(1));

        harness.Stream.State.ShouldBe(StreamState.From(state));
        harness.Stream.SourceUrl.Value.ShouldBe(AddressB);
        harness.Gateway.RepointCalls.Select(call => call.Source).ShouldBe([AddressB]);
    }

    [Fact]
    public async Task The_same_instant_written_in_another_offset_is_equal_and_one_tick_earlier_in_any_offset_is_older()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        var recorded = new DateTimeOffset(2026, 10, 14, 11, 0, 0, TimeSpan.FromHours(2));

        await harness.HandleAsync(AddressA, recorded);
        await harness.HandleAsync(AddressB, recorded.ToOffset(TimeSpan.FromHours(-5)));

        harness.Stream.SourceUrl.Value.ShouldBe(AddressB);

        await harness.HandleAsync(AddressC, recorded.ToOffset(TimeSpan.FromHours(14)).AddTicks(-1));

        harness.Stream.SourceUrl.Value.ShouldBe(AddressB);
        harness.Stream.SourceChangedAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        harness.Gateway.RepointCalls.Select(call => call.Source).ShouldBe([AddressA, AddressB]);
    }

    [Fact]
    public async Task The_earliest_representable_instant_applies_to_a_stream_with_nothing_recorded_and_is_then_superseded()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressA, DateTimeOffset.MinValue);
        await harness.HandleAsync(AddressB, Moment);
        await harness.HandleAsync(AddressC, DateTimeOffset.MinValue);

        harness.Stream.SourceUrl.Value.ShouldBe(AddressB);
        harness.Gateway.RepointCalls.Select(call => call.Source).ShouldBe([AddressA, AddressB]);
    }

    [Theory]
    [InlineData("rtsp://camera-sim:8554/feed?token=SECRET-MARKER")]
    [InlineData("rtsp://camera-sim:8554/feed;SECRET-MARKER")]
    [InlineData("rtsp://camera-sim:8554/feed#SECRET-MARKER")]
    [InlineData("rtsp://[::1]:8554/SECRET-MARKER")]
    [InlineData("rtsp://camera-sim:8554/%53ECRET-MARKER")]
    public async Task An_ignored_hostile_address_leaves_no_trace_of_its_path_query_or_look_alike_host_in_any_log_line(string hostile)
    {
        var harness = new Harness();
        await harness.SeedAsync();
        await harness.HandleAsync(AddressB, Moment.AddSeconds(2));

        var logged = await harness.LoggedWhileAsync(() => harness.HandleAsync(hostile, Moment.AddSeconds(1)));

        var ignored = logged.ShouldHaveSingleItem();
        ignored.Level.ShouldBe(LogLevel.Information);
        harness.AllMessages.ShouldAllBe(
            message => !message.Contains(Marker, StringComparison.OrdinalIgnoreCase)
                && !message.Contains("ECRET-MARKER", StringComparison.OrdinalIgnoreCase));
        harness.Stream.SourceUrl.Value.ShouldBe(AddressB);
        harness.Gateway.RepointCalls.Select(call => call.Source).ShouldBe([AddressB]);
    }

    [Theory]
    [InlineData("rtsp://admin:SECRET-MARKER@camera-sim:8554/a")]
    [InlineData("rtsp://camera-sim\\SECRET-MARKER@elsewhere/x")]
    [InlineData("rtsp://camera-sim:8554/x#@admin:SECRET-MARKER")]
    [InlineData("rtsp://camera-sim/SECRET-MARKER@elsewhere")]
    [InlineData("rtsp://camera-sim/x?u=admin:SECRET-MARKER@elsewhere")]
    [InlineData("rtsp://SECRET-MARKER.cаmera-sim:8554/x")]
    public async Task An_older_change_whose_address_this_context_rejects_fails_rather_than_being_ignored(string rejected)
    {
        var harness = new Harness();
        await harness.SeedAsync();
        await harness.HandleAsync(AddressB, Moment.AddSeconds(2));

        var commandLogBefore = harness.Logger.Entries.Count;

        await Should.ThrowAsync<InvalidOperationException>(
            () => harness.HandleAsync(rejected, Moment.AddSeconds(1)));

        harness.Logger.Entries.Count.ShouldBe(commandLogBefore);
        harness.EventLogger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
        harness.AllMessages.ShouldAllBe(
            message => !message.Contains(Marker, StringComparison.OrdinalIgnoreCase));
        harness.Stream.SourceUrl.Value.ShouldBe(AddressB);
    }
}
