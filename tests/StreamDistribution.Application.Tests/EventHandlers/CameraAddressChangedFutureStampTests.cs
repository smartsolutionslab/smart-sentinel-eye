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
/// Spec 385 (#2991) addendum: a far-future <c>ChangedAt</c> must not pin a
/// stream. The receiver's clock (here a <see cref="FixedClock"/> at
/// <see cref="Moment"/>) bounds how far ahead a stamp may claim to be: beyond
/// the tolerance (default five minutes) it is clamped to the receiver's now,
/// so the repoint still applies but cannot outrank every later change.
/// These cover the default tolerance and compile against today's code; the
/// configurable-tolerance cases (C7, C8) need the options type and are kept as
/// untracked <c>*.cs.pending</c> files until it exists.
/// </summary>
public class CameraAddressChangedFutureStampTests
{
    private const string OriginalUrl = "rtsp://camera-sim:8554/original";

    private const string AddressB = "rtsp://camera-sim:8554/b";

    private const string HostileUrl = "rtsp://10.0.8.1:8554/SECRET-path?token=SECRET";

    private const string HostileOrigin = "rtsp://10.0.8.1:8554";

    private const string CameraText = "0197a1b2-0000-7000-8000-0000000000d2";

    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));

    private static readonly Guid CameraGuid = Guid.Parse(CameraText, CultureInfo.InvariantCulture);

    private static readonly CameraIdentifier Camera = CameraIdentifier.From(CameraGuid);

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

        public async Task SeedAsync(SourceChangedAt? recorded = null)
        {
            var stream = new StreamBuilder()
                .ForCamera(Camera)
                .WithSourceUrl(StreamSourceUrl.From(OriginalUrl))
                .ProvisionedBy(AnAdmin)
                .At(Moment)
                .Build();

            if (recorded is not null)
            {
                stream.RepointTo(StreamSourceUrl.From(OriginalUrl), recorded, new FixedClock(Moment));
            }

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
    }

    private static string Stamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    [Fact]
    public async Task C1_A_maximum_stamp_is_clamped_to_now_the_repoint_applies_and_a_warning_names_both_stamps_without_the_secret()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(HostileUrl, DateTimeOffset.MaxValue);

        harness.SourceUrl.ShouldBe(HostileUrl);
        harness.Stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(Moment));
        var warning = harness.Logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        warning.Message.ShouldContain(CameraText);
        warning.Message.ShouldContain(HostileOrigin);
        warning.Message.ShouldContain(Stamp(DateTimeOffset.MaxValue));
        warning.Message.ShouldContain(Stamp(Moment));
        harness.Logger.Entries.ShouldAllBe(
            logged => !logged.Message.Contains("SECRET", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task C2_A_legitimate_change_after_a_clamped_maximum_stamp_still_applies_the_stream_is_not_pinned()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        await harness.HandleAsync(HostileUrl, DateTimeOffset.MaxValue);

        await harness.HandleAsync(AddressB, Moment.AddSeconds(1));

        harness.SourceUrl.ShouldBe(AddressB);
        harness.Stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(Moment.AddSeconds(1)));
    }

    [Fact]
    public async Task C3_A_stamp_within_the_tolerance_is_not_clamped_and_records_as_given()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressB, Moment.AddMinutes(4));

        harness.Stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(Moment.AddMinutes(4)));
        harness.Logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task C4_A_stamp_exactly_at_now_plus_the_tolerance_is_not_clamped()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressB, Moment + DefaultTolerance);

        harness.Stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(Moment + DefaultTolerance));
        harness.Logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task C4_A_stamp_one_tick_beyond_now_plus_the_tolerance_is_clamped_to_now()
    {
        var harness = new Harness();
        await harness.SeedAsync();

        await harness.HandleAsync(AddressB, (Moment + DefaultTolerance).AddTicks(1));

        harness.SourceUrl.ShouldBe(AddressB);
        harness.Stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(Moment));
        harness.Logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task C5_An_older_change_against_a_recorded_stamp_later_than_now_is_ignored_and_logged_at_warning_with_both_stamps()
    {
        var harness = new Harness();
        var recorded = Moment.AddHours(1);
        await harness.SeedAsync(SourceChangedAt.From(recorded));

        await harness.HandleAsync(HostileUrl, Moment);

        harness.SourceUrl.ShouldBe(OriginalUrl);
        harness.Gateway.RepointCalls.ShouldBeEmpty();
        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(CameraText);
        entry.Message.ShouldContain(HostileOrigin);
        entry.Message.ShouldContain(Stamp(Moment));
        entry.Message.ShouldContain(Stamp(recorded));
        entry.Message.ShouldNotContain("SECRET", Case.Insensitive);
    }

    [Fact]
    public async Task C6_An_older_change_against_a_recorded_stamp_not_later_than_now_is_ignored_at_information()
    {
        var harness = new Harness();
        await harness.SeedAsync(SourceChangedAt.From(Moment.AddSeconds(5)));

        await harness.HandleAsync(AddressB, Moment);

        harness.SourceUrl.ShouldBe(OriginalUrl);
        var entry = harness.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
    }
}
