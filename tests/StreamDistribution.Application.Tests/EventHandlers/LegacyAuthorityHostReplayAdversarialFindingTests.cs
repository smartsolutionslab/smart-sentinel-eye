using System.Globalization;
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
/// Spec 392 / issue #3012, adversarial finding (T070), red against the fix. A replayed <c>CameraRegisteredV1</c> or
/// <c>CameraAddressChangedV1</c> whose address has an '@' in the text after the closing ']' is provisioned or
/// re-pointed today, with the password in it handed to the gateway: <c>StreamSourceUrl.From</c> accepts it because
/// <c>UrlAuthority.TextFollowsTheClosingBracket</c> ends the userinfo at the last '@', not the first. It must fail
/// the message, save nothing and call no gateway, as every other refused shape does.
/// </summary>
public class LegacyAuthorityHostReplayAdversarialFindingTests
{
    private const string OriginalAddress = "rtsp://camera-sim:8554/b";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));

    private static readonly Guid CameraGuid = Guid.Parse("0197a1b2-0000-7000-8000-0000000000e6", CultureInfo.InvariantCulture);

    private static readonly EventMetadata Metadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000ae", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-10-14T08:00:00Z", CultureInfo.InvariantCulture),
        "munich",
        null);

    private readonly InMemoryStreamRepository repository = new();

    private readonly FakeRtspGateway gateway = new();

    public static TheoryData<string> AtSignAfterTheClosingBracket =>
    [
        "rtsp://@[fe80::1]admin:4242@cam/s",
        "rtsp://[fe80::1]admin:4242@",
    ];

    [Theory]
    [MemberData(nameof(AtSignAfterTheClosingBracket))]
    public async Task A_replayed_registration_with_an_at_sign_after_the_closing_bracket_fails_and_provisions_nothing(
        string address)
    {
        var handler = new CameraRegisteredIntegrationEventHandler(
            new ProvisionStreamCommandHandler(
                repository,
                gateway,
                new FixedClock(Moment),
                new CapturingLogger<ProvisionStreamCommandHandler>()),
            new CapturingLogger<CameraRegisteredIntegrationEventHandler>());

        await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            new CameraRegisteredV1(CameraGuid, "Line-1", address, Moment, AnAdmin.Value, Metadata),
            CancellationToken.None));

        repository.Streams.ShouldBeEmpty();
        gateway.AddCalls.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AtSignAfterTheClosingBracket))]
    public async Task A_replayed_address_change_with_an_at_sign_after_the_closing_bracket_fails_and_leaves_the_stream(
        string address)
    {
        var stream = new StreamBuilder()
            .ForCamera(CameraIdentifier.From(CameraGuid))
            .WithSourceUrl(StreamSourceUrl.From(OriginalAddress))
            .ProvisionedBy(AnAdmin)
            .At(Moment)
            .Build();
        repository.Add(stream);
        await repository.SaveAsync(CancellationToken.None);
        var handler = new CameraAddressChangedIntegrationEventHandler(
            new RepointStreamCommandHandler(
                repository,
                gateway,
                new ChangedAtPolicy(new FixedClock(Moment.AddHours(1)), Options.Create(new StreamRepointOptions())),
                new CapturingLogger<RepointStreamCommandHandler>()),
            new CapturingLogger<CameraAddressChangedIntegrationEventHandler>());

        await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            new CameraAddressChangedV1(
                CameraGuid, "munich", OriginalAddress, address, Moment.AddSeconds(1), AnAdmin.Value, Metadata),
            CancellationToken.None));

        repository.Streams.Single().SourceUrl.Value.ShouldBe(OriginalAddress);
        gateway.RepointCalls.ShouldBeEmpty();
    }
}
