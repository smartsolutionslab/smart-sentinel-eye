using System.Globalization;
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
/// Spec 385 (#2991): an address change older than the one a stream already
/// applied must not re-point it back. The real event handler runs over the
/// real command handler; only the ports are fakes. R1-R4 are red on today's
/// code, which applies every change in arrival order; G1-G3 are guards for the
/// "equal applies" half of the rule and are green by nature.
/// </summary>
public class CameraAddressChangedOrderingTests
{
    private const string OriginalUrl = "rtsp://camera-sim:8554/original";

    private const string AddressA = "rtsp://camera-sim:8554/a";

    private const string AddressB = "rtsp://camera-sim:8554/b";

    private const string AddressC = "rtsp://camera-sim:8554/c";

    private const string CameraText = "0197a1b2-0000-7000-8000-0000000000d2";

    private const string PathText = "cam-0197a1b2-0000-7000-8000-0000000000d2";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset T1 = Moment;

    private static readonly DateTimeOffset T2 = Moment.AddSeconds(5);

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

        public string SourceUrl => Repository.Streams.Single().SourceUrl.Value;

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
    public async Task An_older_change_retried_after_a_newer_one_leaves_the_stream_and_the_sfu_at_the_newer_address()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        harness.FailGatewayOnce();

        await Should.ThrowAsync<InvalidOperationException>(() => harness.HandleAsync(AddressA, T1));
        await harness.HandleAsync(AddressB, T2);
        await harness.HandleAsync(AddressA, T1);

        harness.SourceUrl.ShouldBe(AddressB);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressB)]);
    }

    [Fact]
    public async Task An_older_change_after_a_newer_one_is_ignored_on_a_stream_with_nothing_recorded()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressB, T2);
        await harness.HandleAsync(AddressA, T1);

        harness.SourceUrl.ShouldBe(AddressB);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressB)]);
    }

    [Fact]
    public async Task A_newer_change_back_to_the_current_address_still_supersedes_an_older_one()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(OriginalUrl, T2);
        await harness.HandleAsync(AddressB, T1);

        harness.SourceUrl.ShouldBe(OriginalUrl);
        harness.Gateway.RepointCalls.ShouldBe([(Path, OriginalUrl)]);
    }

    [Fact]
    public async Task An_ignored_change_is_logged_at_information_with_its_origin_only()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        await harness.HandleAsync(AddressB, T2);
        var entriesBefore = harness.Logger.Entries.Count;
        var savesBefore = harness.Repository.SaveCallCount;

        await harness.HandleAsync("rtsp://10.0.8.1:8554/SECRET-path?token=SECRET", T1);

        var added = harness.Logger.Entries.Skip(entriesBefore).ToList();
        var entry = added.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe(
            $"Camera {CameraText} address change to rtsp://10.0.8.1:8554 made at 2026-10-14T09:00:00.0000000+00:00 is older than the change its stream already applied; ignored.");
        harness.Logger.Entries.ShouldAllBe(
            logged => !logged.Message.Contains("SECRET", StringComparison.OrdinalIgnoreCase));
        harness.Repository.SaveCallCount.ShouldBe(savesBefore);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressB)]);
    }

    [Fact]
    public async Task Two_changes_with_the_same_timestamp_apply_in_arrival_order()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressB, T1);
        await harness.HandleAsync(AddressC, T1);

        harness.SourceUrl.ShouldBe(AddressC);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressB), (Path, AddressC)]);
    }

    [Fact]
    public async Task A_redelivered_change_after_a_failed_patch_finishes_the_repoint()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        harness.FailGatewayOnce();

        await Should.ThrowAsync<InvalidOperationException>(() => harness.HandleAsync(AddressA, T1));
        await harness.HandleAsync(AddressA, T1);

        harness.SourceUrl.ShouldBe(AddressA);
        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressA)]);
    }

    [Fact]
    public async Task A_redelivered_change_after_a_successful_patch_patches_again()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressA, T1);
        await harness.HandleAsync(AddressA, T1);

        harness.Gateway.RepointCalls.ShouldBe([(Path, AddressA), (Path, AddressA)]);
    }
}
