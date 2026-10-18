using System.Globalization;
using Microsoft.Extensions.Logging;
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
/// Spec 389 / issue #2999, red. A <c>CameraRegisteredV1</c> or <c>CameraAddressChangedV1</c> published before the
/// camera URL value objects started refusing a raw '@' after the authority (still in the outbox at deploy, or
/// replayed from the error queue) can carry an address the new rule refuses. The real event handler over the real
/// command handler must fail the message, write nothing, call no gateway and log the code but never the address.
/// Failing is deliberate: no failure policy is added, so Wolverine's default (three attempts, then the error
/// queue, pinned by <c>CameraEventFailurePolicyCharacterisationTests</c>) bounds the replay.
/// </summary>
public class LegacyMisreadSourceReplayTests
{
    private const string MisreadAddress = "rtsp://admin:12345#@cam/s";

    private const string OriginalAddress = "rtsp://camera-sim:8554/b";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));

    private static readonly Guid CameraGuid = Guid.Parse("0197a1b2-0000-7000-8000-0000000000e4", CultureInfo.InvariantCulture);

    private static readonly EventMetadata Metadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000ac", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-10-14T08:00:00Z", CultureInfo.InvariantCulture),
        "munich",
        null);

    private readonly InMemoryStreamRepository repository = new();

    private readonly FakeRtspGateway gateway = new();

    private readonly CapturingLogger<ProvisionStreamCommandHandler> provisionLogger = new();

    private readonly CapturingLogger<CameraRegisteredIntegrationEventHandler> registeredLogger = new();

    private readonly CapturingLogger<RepointStreamCommandHandler> repointLogger = new();

    private readonly CapturingLogger<CameraAddressChangedIntegrationEventHandler> changedLogger = new();

    [Fact]
    public async Task A_registration_carrying_a_misread_address_fails_and_provisions_nothing()
    {
        var handler = new CameraRegisteredIntegrationEventHandler(
            new ProvisionStreamCommandHandler(repository, gateway, new FixedClock(Moment), provisionLogger),
            registeredLogger);

        await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            new CameraRegisteredV1(CameraGuid, "Line-1", MisreadAddress, Moment, AnAdmin.Value, Metadata),
            CancellationToken.None));

        repository.Streams.ShouldBeEmpty();
        gateway.AddCalls.ShouldBeEmpty();
        var warning = registeredLogger.Entries.Where(entry => entry.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain("STREAM_INVALID_RTSP_SOURCE");
        EveryLoggedText().ShouldAllBe(text => !ContainsTheSecret(text));
    }

    [Fact]
    public async Task A_newer_address_change_carrying_a_misread_address_fails_and_leaves_the_stream_where_it_was()
    {
        var stream = new StreamBuilder()
            .ForCamera(CameraIdentifier.From(CameraGuid))
            .WithSourceUrl(StreamSourceUrl.From(OriginalAddress))
            .ProvisionedBy(AnAdmin)
            .At(Moment)
            .Build();
        repository.Add(stream);
        await repository.SaveAsync(CancellationToken.None);
        var savesBefore = repository.SaveCallCount;
        var handler = new CameraAddressChangedIntegrationEventHandler(
            new RepointStreamCommandHandler(
                repository,
                gateway,
                new FixedClock(Moment.AddHours(1)),
                repointLogger),
            changedLogger);

        await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            new CameraAddressChangedV1(
                CameraGuid, "munich", OriginalAddress, MisreadAddress, Moment.AddSeconds(1), AnAdmin.Value, Metadata),
            CancellationToken.None));

        repository.Streams.Single().SourceUrl.Value.ShouldBe(OriginalAddress);
        repository.SaveCallCount.ShouldBe(savesBefore);
        gateway.RepointCalls.ShouldBeEmpty();
        changedLogger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("STREAM_INVALID_RTSP_SOURCE", StringComparison.Ordinal));
        EveryLoggedText().ShouldAllBe(text => !ContainsTheSecret(text));
    }

    private static bool ContainsTheSecret(string text) =>
        text.Contains("12345", StringComparison.Ordinal)
        || text.Contains("admin", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> EveryLoggedText() =>
        provisionLogger.Entries
            .Concat(registeredLogger.Entries)
            .Concat(repointLogger.Entries)
            .Concat(changedLogger.Entries)
            .SelectMany(entry => new[] { entry.Message, entry.Exception?.ToString() ?? string.Empty });
}
