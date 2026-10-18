using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.EventHandlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Tests.Stream.Builders;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.EventHandlers;

/// <summary>
/// Spec 403 (#3008), adversarial pass over the real event handler and the real command handler (plan §8.4).
/// Only the ports are fakes; messages are built from JSON, as <c>CameraAddressChangedVersionOrderingTests</c>
/// builds them.
///
/// <para>
/// The fuzz facts take a catalogue of versioned changes, deliver them in a seeded random order with
/// redeliveries, and fail the gateway on random first attempts. Every failed delivery is redelivered later,
/// which is what an operator replaying the error queue does (Wolverine dead-letters on the first failure). The
/// outcome must be the highest version, and MediaMTX's last successful instruction must be that change's
/// address.
/// </para>
///
/// <para>
/// <c>Residual_</c> facts are green by design and pin outcomes spec 403 §8 accepts. The forged-high-version
/// residual (R-V1) is accepted by the owner and is deliberately not exercised here.
/// </para>
/// </summary>
public class CameraAddressChangedVersionOrderingAdversarialTests
{
    private const string OriginalUrl = "rtsp://camera-sim:8554/original";

    private const string CameraText = "0197a1b2-0000-7000-8000-0000000000e3";

    private const string PathText = "cam-0197a1b2-0000-7000-8000-0000000000e3";

    private const int Sequences = 1500;

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));

    private static readonly Guid CameraGuid = Guid.Parse(CameraText, CultureInfo.InvariantCulture);

    private static readonly CameraIdentifier Camera = CameraIdentifier.From(CameraGuid);

    private static readonly MediaMtxPath Path = MediaMtxPath.From(PathText);

    private static readonly EventMetadata Metadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000ab", CultureInfo.InvariantCulture),
        Moment,
        "munich",
        null);

    private sealed record Change(string Url, DateTimeOffset ChangedAt, int? Version);

    private sealed class CountingStreamRepository(InMemoryStreamRepository inner) : IStreamRepository
    {
        public int Lookups { get; private set; }

        public Task<Option<Domain.Stream.Stream>> GetByIdentifierAsync(StreamIdentifier stream, CancellationToken cancellationToken)
        {
            Lookups++;
            return inner.GetByIdentifierAsync(stream, cancellationToken);
        }

        public Task<Option<Domain.Stream.Stream>> GetByCameraAsync(CameraIdentifier camera, CancellationToken cancellationToken)
        {
            Lookups++;
            return inner.GetByCameraAsync(camera, cancellationToken);
        }

        public Task<Option<Domain.Stream.Stream>> GetByPathAsync(MediaMtxPath path, CancellationToken cancellationToken)
        {
            Lookups++;
            return inner.GetByPathAsync(path, cancellationToken);
        }

        public void Add(Domain.Stream.Stream stream) => inner.Add(stream);

        public Task SaveAsync(CancellationToken cancellationToken) => inner.SaveAsync(cancellationToken);

        public Task<bool> IsUnchangedSinceLoadAsync(Domain.Stream.Stream stream, CancellationToken cancellationToken) =>
            inner.IsUnchangedSinceLoadAsync(stream, cancellationToken);

        public Task<StreamState> ReadCommittedStateAsync(StreamIdentifier stream, CancellationToken cancellationToken) =>
            inner.ReadCommittedStateAsync(stream, cancellationToken);
    }

    private sealed class Harness
    {
        public Harness()
        {
            Repository = new CountingStreamRepository(Store);
            var commandHandler = new RepointStreamCommandHandler(
                Repository,
                Gateway,
                new FixedClock(Moment),
                Logger);
            Handler = new CameraAddressChangedIntegrationEventHandler(
                commandHandler,
                NullLogger<CameraAddressChangedIntegrationEventHandler>.Instance);
        }

        public InMemoryStreamRepository Store { get; } = new();

        public CountingStreamRepository Repository { get; }

        public FakeRtspGateway Gateway { get; } = new();

        public CapturingLogger<RepointStreamCommandHandler> Logger { get; } = new();

        public CameraAddressChangedIntegrationEventHandler Handler { get; }

        public Domain.Stream.Stream Stream => Store.Streams.Single();

        public string SourceUrl => Stream.SourceUrl.Value;

        public async Task SeedAsync()
        {
            var stream = new StreamBuilder()
                .ForCamera(Camera)
                .WithSourceUrl(StreamSourceUrl.From(OriginalUrl))
                .ProvisionedBy(AnAdmin)
                .At(Moment)
                .Build();

            Store.Add(stream);
            await Store.SaveAsync(CancellationToken.None);
        }

        public Task HandleAsync(Change change) =>
            Handler.Handle(FromTheWire(change.Url, change.ChangedAt, change.Version), CancellationToken.None);

        public Task HandleAsync(string url, DateTimeOffset changedAt, int? previousVersion) =>
            Handler.Handle(FromTheWire(url, changedAt, previousVersion), CancellationToken.None);

        private static CameraAddressChangedV1 FromTheWire(string url, DateTimeOffset changedAt, int? previousVersion)
        {
            var sent = new CameraAddressChangedV1(
                Camera: CameraGuid,
                Fab: "munich",
                PreviousUrl: OriginalUrl,
                Url: url,
                ChangedAt: changedAt,
                ChangedBy: AnAdmin.Value,
                Metadata: Metadata);

            var json = JsonSerializer.SerializeToNode(sent)!.AsObject();
            json.Remove("PreviousVersion");

            if (previousVersion is { } version)
            {
                json["PreviousVersion"] = version;
            }

            return JsonSerializer.Deserialize<CameraAddressChangedV1>(json.ToJsonString())!;
        }
    }

    [Fact]
    public async Task Random_arrival_orders_with_redeliveries_and_gateway_failures_end_on_the_highest_version()
    {
        for (var seed = 0; seed < Sequences; seed++)
        {
            var random = new Random(seed);
            var changes = Catalogue(random);
            Queue<Change> pending = new(Shuffle(random, [.. changes, .. Redeliveries(random, changes)]));
            var harness = new Harness();
            await harness.SeedAsync();
            HashSet<int> failNextAttempt = [];
            List<int> toldVersions = [];

            harness.Gateway.OnRepointPath = (_, url) =>
            {
                var version = changes.Single(change => change.Url == url).Version!.Value;
                if (failNextAttempt.Remove(version))
                {
                    throw new HttpRequestException("sfu unreachable");
                }
            };

            List<Change> handled = [];
            while (pending.TryDequeue(out var change))
            {
                if (random.Next(4) == 0)
                {
                    failNextAttempt.Add(change.Version!.Value);
                }

                handled.Add(change);
                try
                {
                    await harness.HandleAsync(change);
                }
                catch (InvalidOperationException)
                {
                    pending.Enqueue(change);
                }

                failNextAttempt.Remove(change.Version!.Value);
            }

            var latest = changes.MaxBy(change => change.Version)!;
            var where = $"seed {seed}: handled {string.Join(" ; ", handled.Select(change => $"v{change.Version}"))}";

            harness.SourceUrl.ShouldBe(latest.Url, where);
            harness.Stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(latest.Version!.Value), where);
            harness.Gateway.RepointCalls.ShouldNotBeEmpty(where);
            harness.Gateway.RepointCalls[^1].Source.ShouldBe(latest.Url, where);

            toldVersions.AddRange(harness.Gateway.RepointCalls
                .Select(call => changes.Single(change => change.Url == call.Source).Version!.Value));
            toldVersions.ShouldBe([.. toldVersions.Order()], where);
        }
    }

    [Fact]
    public async Task An_ignored_versioned_change_is_never_saved_and_never_reaches_the_gateway_however_often_it_arrives()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        await harness.HandleAsync("rtsp://camera-sim:8554/v9", Moment, previousVersion: 9);
        var savesBefore = harness.Store.SaveCallCount;

        for (var version = 0; version < 9; version++)
        {
            await harness.HandleAsync($"rtsp://camera-sim:8554/v{version}", Moment.AddHours(version + 1), version);
        }

        harness.Store.SaveCallCount.ShouldBe(savesBefore);
        harness.Gateway.RepointCalls.ShouldBe([(Path, "rtsp://camera-sim:8554/v9")]);
        harness.Logger.Entries.Count(entry => entry.Level == LogLevel.Information
            && entry.Message.Contains("made against camera version", StringComparison.Ordinal)).ShouldBe(9);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task A_negative_version_is_refused_before_the_stream_is_looked_up(int version)
    {
        var harness = new Harness();
        await harness.SeedAsync();
        var savesBefore = harness.Store.SaveCallCount;

        await Should.ThrowAsync<ArgumentException>(
            () => harness.HandleAsync("rtsp://camera-sim:8554/forged", Moment, version));

        harness.Repository.Lookups.ShouldBe(0);
        harness.Store.SaveCallCount.ShouldBe(savesBefore);
        harness.Gateway.RepointCalls.ShouldBeEmpty();
        harness.SourceUrl.ShouldBe(OriginalUrl);
    }

    [Fact]
    public async Task A_negative_version_with_an_invalid_address_is_refused_for_the_version_before_the_address_is_read()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        var refused = await Should.ThrowAsync<ArgumentException>(
            () => harness.HandleAsync("http://not-rtsp.example/stream", Moment, -1));

        refused.Message.ShouldNotContain("not-rtsp.example");
        harness.Repository.Lookups.ShouldBe(0);
    }

    [Fact]
    public async Task An_invalid_address_with_a_valid_version_is_refused_before_lookup_and_records_nothing()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        await harness.HandleAsync("rtsp://camera-sim:8554/v1", Moment, previousVersion: 1);
        var lookupsBefore = harness.Repository.Lookups;

        await Should.ThrowAsync<InvalidOperationException>(
            () => harness.HandleAsync("http://not-rtsp.example/stream", Moment.AddHours(1), 5));

        harness.Repository.Lookups.ShouldBe(lookupsBefore);
        harness.Stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(1));
    }

    [Fact]
    public async Task Version_zero_and_int_max_cross_the_handler_intact()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync("rtsp://camera-sim:8554/zero", Moment, previousVersion: 0);
        harness.Stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(0));

        await harness.HandleAsync("rtsp://camera-sim:8554/max", Moment.AddDays(-1), previousVersion: int.MaxValue);
        harness.Stream.SourceCameraVersion.ShouldBe(SourceCameraVersion.From(int.MaxValue));
        harness.SourceUrl.ShouldBe("rtsp://camera-sim:8554/max");
    }

    [Fact]
    public async Task A_retired_stream_skips_a_versioned_change_without_saving_or_recording_it()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        harness.Stream.Retire(new FixedClock(Moment));
        var savesBefore = harness.Store.SaveCallCount;

        await harness.HandleAsync("rtsp://camera-sim:8554/after-retire", Moment, previousVersion: 3);

        harness.Store.SaveCallCount.ShouldBe(savesBefore);
        harness.Stream.SourceCameraVersion.ShouldBeNull();
        harness.Gateway.RepointCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_versioned_change_for_a_camera_without_a_stream_succeeds_and_saves_nothing()
    {
        var harness = new Harness();

        await harness.HandleAsync("rtsp://camera-sim:8554/nobody", Moment, previousVersion: 3);

        harness.Store.SaveCallCount.ShouldBe(0);
        harness.Gateway.RepointCalls.ShouldBeEmpty();
    }

    /// <summary>
    /// A gateway failure after the save leaves the higher version recorded but MediaMTX on the old source. A
    /// lower-version message arriving in between is ignored, so it cannot repair MediaMTX either; only the
    /// failed change's own redelivery does. Pins that the ignore path really is a no-op on the gateway.
    /// </summary>
    [Fact]
    public async Task After_a_failed_repoint_an_older_change_does_not_touch_the_gateway_and_the_redelivery_finishes_it()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        var failed = false;
        harness.Gateway.OnRepointPath = (_, _) =>
        {
            if (!failed)
            {
                failed = true;
                throw new HttpRequestException("sfu unreachable");
            }
        };

        await Should.ThrowAsync<InvalidOperationException>(
            () => harness.HandleAsync("rtsp://camera-sim:8554/v5", Moment, 5));
        await harness.HandleAsync("rtsp://camera-sim:8554/v4", Moment.AddMinutes(1), 4);

        harness.Gateway.RepointCalls.ShouldBeEmpty();

        await harness.HandleAsync("rtsp://camera-sim:8554/v5", Moment, 5);

        harness.Gateway.RepointCalls.ShouldBe([(Path, "rtsp://camera-sim:8554/v5")]);
    }

    /// <summary>
    /// Residual R-V2 through the full stack, the downgrade. Catalogue order: A (v3), B (v4), then C from a
    /// CameraCatalog replica still on the old code (no version). Arrival: B, C, A. C clears the recorded
    /// version; A then applies under rule 2 and MediaMTX is told A last. The catalogue says C.
    /// </summary>
    [Fact]
    public async Task Residual_a_version_less_change_clears_the_version_and_an_older_versioned_redelivery_then_wins()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync("rtsp://camera-sim:8554/b", Moment, 4);
        await harness.HandleAsync("rtsp://camera-sim:8554/c", Moment.AddSeconds(5), null);
        await harness.HandleAsync("rtsp://camera-sim:8554/a", Moment.AddSeconds(-30), 3);

        harness.SourceUrl.ShouldBe("rtsp://camera-sim:8554/a");
        harness.Gateway.RepointCalls[^1].ShouldBe((Path, "rtsp://camera-sim:8554/a"));
    }

    /// <summary>
    /// Residual, outside R-V2's wording: no replica overlap, one CameraCatalog. L was made before the deploy
    /// while the clock ran ten minutes fast and failed at the gateway (dead-lettered); after the deploy and a
    /// clock correction V (v7) is made and applied; the error queue is replayed. L wins by stamp and the stream
    /// is left on the older address while the API answered 204 for V - #3008's own symptom.
    /// </summary>
    [Fact]
    public async Task Residual_a_dead_lettered_version_less_change_replayed_after_a_clock_correction_overrides_a_newer_versioned_change()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        var failed = false;
        harness.Gateway.OnRepointPath = (_, _) =>
        {
            if (!failed)
            {
                failed = true;
                throw new HttpRequestException("sfu unreachable");
            }
        };

        await Should.ThrowAsync<InvalidOperationException>(
            () => harness.HandleAsync("rtsp://camera-sim:8554/l", Moment.AddMinutes(10), null));
        await harness.HandleAsync("rtsp://camera-sim:8554/v", Moment.AddMinutes(5), 7);
        await harness.HandleAsync("rtsp://camera-sim:8554/l", Moment.AddMinutes(10), null);

        harness.SourceUrl.ShouldBe("rtsp://camera-sim:8554/l");
        harness.Stream.SourceCameraVersion.ShouldBeNull();
        harness.Gateway.RepointCalls.ShouldBe(
            [(Path, "rtsp://camera-sim:8554/v"), (Path, "rtsp://camera-sim:8554/l")]);
    }

    private static List<Change> Catalogue(Random random)
    {
        var count = random.Next(1, 6);
        var version = random.Next(0, 3);
        List<Change> changes = [];

        for (var index = 0; index < count; index++)
        {
            var stamp = random.Next(10) == 0 ? DateTimeOffset.MaxValue : Moment.AddMinutes(random.Next(-20, 21));
            changes.Add(new Change($"rtsp://camera-sim:8554/v{version}", stamp, version));
            version += random.Next(1, 3);
        }

        return changes;
    }

    private static List<Change> Redeliveries(Random random, List<Change> changes) =>
        Enumerable.Range(0, random.Next(0, 4)).Select(_ => changes[random.Next(changes.Count)]).ToList();

    private static List<Change> Shuffle(Random random, List<Change> changes) =>
        changes.OrderBy(_ => random.Next()).ToList();
}
