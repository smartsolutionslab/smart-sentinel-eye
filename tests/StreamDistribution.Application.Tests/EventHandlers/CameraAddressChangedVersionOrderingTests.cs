using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Spec 403 (#3008): address changes reach a stream in the order the catalogue
/// made them, decided by the camera version a change was made against and not
/// by wall-clock <c>ChangedAt</c>. The real event handler runs over the real
/// command handler; only the ports are fakes.
///
/// <para>
/// <b>Messages are built as they arrive off the wire.</b> A
/// <see cref="CameraAddressChangedV1"/> is serialised, the
/// <c>PreviousVersion</c> member is added to (or left out of) the JSON object,
/// and the result is deserialised. The file therefore compiles against code
/// that has no such member: today the member is ignored, so V1-V6, V9 and V10
/// fail on their assertions, and G4-G6 are guards that are green by nature (the
/// equal-applies half of rule 1, and rules 3 and 1-with-agreeing-stamps).
/// </para>
/// </summary>
public class CameraAddressChangedVersionOrderingTests
{
    private const string OriginalUrl = "rtsp://camera-sim:8554/original";

    private const string AddressA = "rtsp://camera-sim:8554/a";

    private const string AddressB = "rtsp://camera-sim:8554/b";

    private const string AddressC = "rtsp://camera-sim:8554/c";

    private const string AddressD = "rtsp://camera-sim:8554/d";

    private const string AddressU = "rtsp://camera-sim:8554/u";

    private const string HostileUrl = "rtsp://10.0.8.1:8554/SECRET-path?token=SECRET";

    private const string CameraText = "0197a1b2-0000-7000-8000-0000000000d2";

    private const string PathText = "cam-0197a1b2-0000-7000-8000-0000000000d2";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset T1 = Moment.AddMinutes(-10);

    private static readonly DateTimeOffset T2 = Moment.AddMinutes(-5);

    private static readonly DateTimeOffset T3 = Moment.AddMinutes(-1);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));

    private static readonly Guid CameraGuid = Guid.Parse(CameraText, CultureInfo.InvariantCulture);

    private static readonly CameraIdentifier Camera = CameraIdentifier.From(CameraGuid);

    private static readonly MediaMtxPath Path = MediaMtxPath.From(PathText);

    private static readonly EventMetadata Metadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-10-14T08:00:00Z", CultureInfo.InvariantCulture),
        "munich",
        null);

    private sealed class Harness
    {
        public Harness()
        {
            var commandHandler = new RepointStreamCommandHandler(
                Repository,
                Gateway,
                new FixedClock(Moment),
                Logger);
            Handler = new CameraAddressChangedIntegrationEventHandler(
                commandHandler,
                NullLogger<CameraAddressChangedIntegrationEventHandler>.Instance);
        }

        public InMemoryStreamRepository Repository { get; } = new();

        public FakeRtspGateway Gateway { get; } = new();

        public CapturingLogger<RepointStreamCommandHandler> Logger { get; } = new();

        public CameraAddressChangedIntegrationEventHandler Handler { get; }

        public Domain.Stream.Stream Stream => Repository.Streams.Single();

        public string SourceUrl => Stream.SourceUrl.Value;

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
            DateTimeOffset changedAt,
            int? previousVersion) =>
            Handler.Handle(FromTheWire(url, changedAt, previousVersion), CancellationToken.None);

        private static CameraAddressChangedV1 FromTheWire(
            string url,
            DateTimeOffset changedAt,
            int? previousVersion)
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

        public void FailGatewayOnce()
        {
            var failed = false;
            Gateway.OnRepointPath = (_, _) =>
            {
                if (failed)
                {
                    return;
                }

                failed = true;
                throw new InvalidOperationException("sfu unreachable");
            };
        }
    }

    [Fact]
    public async Task A_later_change_stamped_earlier_after_a_backward_clock_step_applies()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressB, Moment.AddMinutes(-50), previousVersion: 3);
        await harness.HandleAsync(AddressC, Moment.AddMinutes(-55), previousVersion: 4);

        harness.SourceUrl.ShouldBe(AddressC);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressB), (Path, AddressC)]);
    }

    [Fact]
    public async Task An_older_change_redelivered_after_a_newer_one_is_ignored_whatever_its_stamp()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        harness.FailGatewayOnce();

        await Should.ThrowAsync<InvalidOperationException>(() => harness.HandleAsync(AddressA, T2, previousVersion: 3));
        await harness.HandleAsync(AddressB, T1, previousVersion: 4);
        await harness.HandleAsync(AddressA, T2, previousVersion: 3);

        harness.SourceUrl.ShouldBe(AddressB);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressB)]);
    }

    [Fact]
    public async Task A_stale_retry_does_not_win_when_the_catalogue_runs_six_minutes_fast()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        harness.FailGatewayOnce();

        await Should.ThrowAsync<InvalidOperationException>(
            () => harness.HandleAsync(AddressA, Moment.AddMinutes(6), previousVersion: 3));
        await harness.HandleAsync(AddressB, Moment.AddMinutes(6).AddSeconds(1), previousVersion: 4);
        await harness.HandleAsync(AddressA, Moment.AddMinutes(6), previousVersion: 3);

        harness.SourceUrl.ShouldBe(AddressB);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressB)]);
    }

    [Fact]
    public async Task A_genuine_newer_change_straddling_the_old_tolerance_applies()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressA, Moment.AddMinutes(4).AddSeconds(59), previousVersion: 3);
        await harness.HandleAsync(AddressB, Moment.AddMinutes(5).AddSeconds(3), previousVersion: 4);

        harness.SourceUrl.ShouldBe(AddressB);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressA), (Path, AddressB)]);
    }

    [Fact]
    public async Task A_far_future_stamp_is_recorded_as_given_and_nothing_is_logged_at_warning()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressU, DateTimeOffset.MaxValue, previousVersion: 3);

        harness.SourceUrl.ShouldBe(AddressU);
        harness.Stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(DateTimeOffset.MaxValue));
        harness.Logger.Entries.ShouldAllBe(logged => logged.Level != LogLevel.Warning);

        await harness.HandleAsync(AddressB, Moment, previousVersion: 4);

        harness.SourceUrl.ShouldBe(AddressB);
    }

    [Fact]
    public async Task The_first_versioned_change_applies_over_a_record_made_without_a_version()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressA, Moment, previousVersion: null);
        await harness.HandleAsync(AddressB, Moment.AddMinutes(-10), previousVersion: 0);

        harness.SourceUrl.ShouldBe(AddressB);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressA), (Path, AddressB)]);
    }

    [Fact]
    public async Task An_ignored_older_versioned_change_is_logged_at_information_with_its_origin_and_version_only()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        await harness.HandleAsync(AddressB, T1, previousVersion: 4);
        var entriesBefore = harness.Logger.Entries.Count;
        var savesBefore = harness.Repository.SaveCallCount;

        await harness.HandleAsync(HostileUrl, T2, previousVersion: 3);

        var added = harness.Logger.Entries.Skip(entriesBefore).ToList();
        var entry = added.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe(
            $"Camera {CameraText} address change to rtsp://10.0.8.1:8554 made against camera version 3 is older than the change its stream already applied (camera version 4); ignored.");
        entry.Message.ShouldContain("version 3");
        entry.Message.ShouldContain("(camera version 4)");
        harness.Logger.Entries.ShouldAllBe(
            logged => !logged.Message.Contains("SECRET", StringComparison.OrdinalIgnoreCase));
        harness.Repository.SaveCallCount.ShouldBe(savesBefore);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressB)]);
    }

    [Fact]
    public async Task A_negative_version_is_refused_before_anything_is_looked_up_or_saved()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        var savesBefore = harness.Repository.SaveCallCount;

        await Should.ThrowAsync<ArgumentException>(() => harness.HandleAsync(AddressB, T1, previousVersion: -1));

        harness.SourceUrl.ShouldBe(OriginalUrl);
        harness.Gateway.RepointCalls.ShouldBeEmpty();
        harness.Repository.SaveCallCount.ShouldBe(savesBefore);
    }

    [Fact]
    public async Task A_redelivery_of_the_same_versioned_change_finishes_a_failed_repoint()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        harness.FailGatewayOnce();

        await Should.ThrowAsync<InvalidOperationException>(() => harness.HandleAsync(AddressA, T1, previousVersion: 3));
        await harness.HandleAsync(AddressA, T1, previousVersion: 3);

        harness.SourceUrl.ShouldBe(AddressA);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressA)]);
    }

    [Fact]
    public async Task A_change_without_a_version_over_a_versioned_record_is_ordered_by_its_stamp()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressB, T2, previousVersion: 4);
        await harness.HandleAsync(AddressC, T3, previousVersion: null);
        await harness.HandleAsync(AddressD, T1, previousVersion: null);

        harness.SourceUrl.ShouldBe(AddressC);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressB), (Path, AddressC)]);
    }

    [Fact]
    public async Task An_older_versioned_change_with_an_older_stamp_is_ignored()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        harness.FailGatewayOnce();

        await Should.ThrowAsync<InvalidOperationException>(() => harness.HandleAsync(AddressA, T1, previousVersion: 3));
        await harness.HandleAsync(AddressB, T2, previousVersion: 4);
        await harness.HandleAsync(AddressA, T1, previousVersion: 3);

        harness.SourceUrl.ShouldBe(AddressB);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressB)]);
    }
}
