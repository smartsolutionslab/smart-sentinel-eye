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
/// Spec 392 / issues #3012 and #3017, adversarial (T070). Shapes the written replay tests leave out: an empty host
/// behind odd casing, a longer slash run or nothing at all, and bracket tails hiding a number or an encoded ':'.
/// Each, replayed as a <c>CameraRegisteredV1</c> or a newer <c>CameraAddressChangedV1</c>, fails the message on its
/// first attempt (the handler throws; no failure policy retries it), saves nothing, calls no gateway and logs the
/// code without the address. A plain bracketed address with a port still provisions, so the failure is not a
/// blanket one.
/// </summary>
public class LegacyAuthorityHostReplayAdversarialTests
{
    private const string OriginalAddress = "rtsp://camera-sim:8554/b";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));

    private static readonly Guid CameraGuid = Guid.Parse("0197a1b2-0000-7000-8000-0000000000e5", CultureInfo.InvariantCulture);

    private static readonly EventMetadata Metadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000ad", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-10-14T08:00:00Z", CultureInfo.InvariantCulture),
        "munich",
        null);

    private readonly InMemoryStreamRepository repository = new();

    private readonly FakeRtspGateway gateway = new();

    private readonly CapturingLogger<ProvisionStreamCommandHandler> provisionLogger = new();

    private readonly CapturingLogger<CameraRegisteredIntegrationEventHandler> registeredLogger = new();

    private readonly CapturingLogger<RepointStreamCommandHandler> repointLogger = new();

    private readonly CapturingLogger<CameraAddressChangedIntegrationEventHandler> changedLogger = new();

    public static TheoryData<string> RefusedAddresses =>
    [
        "RtSp:////admin:4242@cam/s",
        "rtsp://////admin:4242@cam/s",
        "rtsp:///4242",
        "rtsp://",
        "rtsp://[fe80::1]x:4242/s",
        "rtsp://[fe80::1]%3A4242/s",
        "rtsp://@[fe80::1]x4242/s",
        "rtsp://[fe80::1%25eth0]4242/s",
    ];

    [Theory]
    [MemberData(nameof(RefusedAddresses))]
    public async Task A_replayed_registration_carrying_such_an_address_fails_on_its_first_attempt_and_provisions_nothing(
        string address)
    {
        var handler = new CameraRegisteredIntegrationEventHandler(
            new ProvisionStreamCommandHandler(repository, gateway, new FixedClock(Moment), provisionLogger),
            registeredLogger);

        await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            new CameraRegisteredV1(CameraGuid, "Line-1", address, Moment, AnAdmin.Value, Metadata),
            CancellationToken.None));

        repository.Streams.ShouldBeEmpty();
        repository.SaveCallCount.ShouldBe(0);
        gateway.AddCalls.ShouldBeEmpty();
        registeredLogger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("STREAM_INVALID_RTSP_SOURCE", StringComparison.Ordinal));
        EveryLoggedText().ShouldAllBe(text => !text.Contains("4242", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RefusedAddresses))]
    public async Task A_replayed_address_change_carrying_such_an_address_fails_on_its_first_attempt_and_leaves_the_stream(
        string address)
    {
        await ProvisionedStream();
        var savesBefore = repository.SaveCallCount;

        await Should.ThrowAsync<InvalidOperationException>(() => ChangedHandler().Handle(
            new CameraAddressChangedV1(
                CameraGuid, "munich", OriginalAddress, address, Moment.AddSeconds(1), AnAdmin.Value, Metadata),
            CancellationToken.None));

        repository.Streams.Single().SourceUrl.Value.ShouldBe(OriginalAddress);
        repository.SaveCallCount.ShouldBe(savesBefore);
        gateway.RepointCalls.ShouldBeEmpty();
        EveryLoggedText().ShouldAllBe(text => !text.Contains("4242", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("rtsp://[fe80::1]:8554/s")]
    [InlineData("rtsp://[FE80::ABCD%25eth0]:554/s")]
    [InlineData("rtsp://cam/p]q")]
    public async Task A_replayed_registration_at_a_plain_address_still_provisions(string address)
    {
        var handler = new CameraRegisteredIntegrationEventHandler(
            new ProvisionStreamCommandHandler(repository, gateway, new FixedClock(Moment), provisionLogger),
            registeredLogger);

        await handler.Handle(
            new CameraRegisteredV1(CameraGuid, "Line-1", address, Moment, AnAdmin.Value, Metadata),
            CancellationToken.None);

        repository.Streams.ShouldHaveSingleItem().SourceUrl.Value.ShouldBe(address);
    }

    private async Task ProvisionedStream()
    {
        var stream = new StreamBuilder()
            .ForCamera(CameraIdentifier.From(CameraGuid))
            .WithSourceUrl(StreamSourceUrl.From(OriginalAddress))
            .ProvisionedBy(AnAdmin)
            .At(Moment)
            .Build();
        repository.Add(stream);
        await repository.SaveAsync(CancellationToken.None);
    }

    private CameraAddressChangedIntegrationEventHandler ChangedHandler() =>
        new(
            new RepointStreamCommandHandler(
                repository,
                gateway,
                new FixedClock(Moment.AddHours(1)),
                repointLogger),
            changedLogger);

    private IEnumerable<string> EveryLoggedText() =>
        provisionLogger.Entries
            .Concat(registeredLogger.Entries)
            .Concat(repointLogger.Entries)
            .Concat(changedLogger.Entries)
            .SelectMany(entry => new[] { entry.Message, entry.Exception?.ToString() ?? string.Empty });
}
