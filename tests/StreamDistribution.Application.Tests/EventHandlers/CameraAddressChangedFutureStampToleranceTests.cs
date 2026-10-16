using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
/// Spec 385 (#2991) addendum, C7: the tolerance is configuration
/// (<c>StreamRepoint:ChangedAtTolerance</c>). With zero, any stamp ahead of the
/// receiver's now is clamped. Untracked until <c>StreamRepointOptions</c>
/// exists; the engineer renames this to <c>.cs</c>. The assumed shape is
/// <c>StreamRepointOptions { TimeSpan ChangedAtTolerance }</c> in
/// <c>SmartSentinelEye.StreamDistribution.Application.Commands</c>, passed to
/// <c>RepointStreamCommandHandler</c> as <c>IOptions&lt;StreamRepointOptions&gt;</c>
/// after the clock; the engineer may adapt the constructor call here (and the
/// namespace), never an assertion.
/// </summary>
public class CameraAddressChangedFutureStampToleranceTests
{
    private const string OriginalUrl = "rtsp://camera-sim:8554/original";

    private const string AddressB = "rtsp://camera-sim:8554/b";

    private const string CameraText = "0197a1b2-0000-7000-8000-0000000000d2";

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

    [Fact]
    public async Task C7_With_a_tolerance_of_zero_any_future_stamp_is_clamped_to_now()
    {
        var repository = new InMemoryStreamRepository();
        var logger = new CapturingLogger<RepointStreamCommandHandler>();
        var commandHandler = new RepointStreamCommandHandler(
            repository,
            new FakeRtspGateway(),
            new ChangedAtPolicy(
                new FixedClock(Moment),
                Options.Create(new StreamRepointOptions { ChangedAtTolerance = TimeSpan.Zero })),
            logger);
        var handler = new CameraAddressChangedIntegrationEventHandler(
            commandHandler,
            NullLogger<CameraAddressChangedIntegrationEventHandler>.Instance);
        repository.Add(new StreamBuilder()
            .ForCamera(Camera)
            .WithSourceUrl(StreamSourceUrl.From(OriginalUrl))
            .ProvisionedBy(AnAdmin)
            .At(Moment)
            .Build());
        await repository.SaveAsync(CancellationToken.None);

        await handler.Handle(
            new CameraAddressChangedV1(
                Camera: CameraGuid,
                Fab: "munich",
                PreviousUrl: OriginalUrl,
                Url: AddressB,
                ChangedAt: Moment.AddTicks(1),
                ChangedBy: AnAdmin.Value,
                Metadata: Metadata),
            CancellationToken.None);

        var stream = repository.Streams.Single();
        stream.SourceUrl.Value.ShouldBe(AddressB);
        stream.SourceChangedAt.ShouldBe(SourceChangedAt.From(Moment));
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
    }
}
