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
/// Spec 394 / issue #3023 items 1, 2 and 5, adversarial, across the deploy. Pins what the written replay rows leave
/// open. A redelivered <c>CameraRegisteredV1</c> for a stream saved before the deploy at a now-refused address fails
/// with the typed failure before the existing-row branch, so it neither re-asserts the gateway path nor writes
/// (the partial "row saved, path not added" a pre-deploy failure left behind is not finished by redelivery). An
/// <em>older</em> <c>CameraAddressChangedV1</c> carrying such an address, acked as an ignored older repoint before the
/// deploy, now fails the message: the source is validated before the stamp is compared. A second delivery of the
/// same message fails the same way and writes nothing. An unreadable host on a newer change fails typed, not as an
/// escaped <see cref="IndexOutOfRangeException"/>. No log line carries the address.
/// </summary>
public class LegacyZoneDelimiterReplayAdversarialTests
{
    private const string HiddenHostAddress = "rtsp://@[fe80::1%@10.0.0.66:8554]/live";

    private const string FragmentAddress = "rtsp://[fe80::1%25eth0#evil]/s";

    private const string UnreadableHostAddress = "rtsp://[1:2:3:4:5:6:7:8:/64]?x";

    private const string OriginalAddress = "rtsp://camera-sim:8554/b";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-17T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000002", CultureInfo.InvariantCulture));

    private static readonly Guid CameraGuid = Guid.Parse("0197a1b2-0000-7000-8000-0000000000e5", CultureInfo.InvariantCulture);

    private static readonly EventMetadata Metadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000ad", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-10-17T08:00:00Z", CultureInfo.InvariantCulture),
        "munich",
        null);

    private readonly InMemoryStreamRepository repository = new();

    private readonly FakeRtspGateway gateway = new();

    private readonly CapturingLogger<ProvisionStreamCommandHandler> provisionLogger = new();

    private readonly CapturingLogger<CameraRegisteredIntegrationEventHandler> registeredLogger = new();

    private readonly CapturingLogger<RepointStreamCommandHandler> repointLogger = new();

    private readonly CapturingLogger<CameraAddressChangedIntegrationEventHandler> changedLogger = new();

    [Fact]
    public async Task A_redelivered_registration_for_a_stream_saved_at_a_now_refused_address_fails_without_re_asserting_the_path()
    {
        await Saved(StreamSourceUrl.FromStored(HiddenHostAddress));
        var savesBefore = repository.SaveCallCount;
        var handler = RegisteredHandler();

        await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            new CameraRegisteredV1(CameraGuid, "Line-1", HiddenHostAddress, Moment, AnAdmin.Value, Metadata),
            CancellationToken.None));

        gateway.AddCalls.ShouldBeEmpty();
        repository.SaveCallCount.ShouldBe(savesBefore);
        repository.Streams.Single().SourceUrl.Value.ShouldBe(HiddenHostAddress);
        registeredLogger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("STREAM_INVALID_RTSP_SOURCE", StringComparison.Ordinal));
        EveryLoggedText().ShouldAllBe(text => !ContainsTheSecret(text));
    }

    [Fact]
    public async Task An_older_address_change_carrying_a_delimiter_in_the_zone_now_fails_the_message_instead_of_being_acked()
    {
        var stream = await Saved(StreamSourceUrl.From(OriginalAddress));
        stream.RepointTo(
            StreamSourceUrl.From("rtsp://camera-sim:8554/c"),
            SourceChangedAt.From(Moment.AddHours(2)),
            Option<SourceCameraVersion>.None,
            new FixedClock(Moment.AddHours(2)));
        var savesBefore = repository.SaveCallCount;
        var handler = ChangedHandler();

        await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            Change(FragmentAddress, Moment.AddHours(1)),
            CancellationToken.None));

        repository.Streams.Single().SourceUrl.Value.ShouldBe("rtsp://camera-sim:8554/c");
        repository.SaveCallCount.ShouldBe(savesBefore);
        gateway.RepointCalls.ShouldBeEmpty();
        EveryLoggedText().ShouldAllBe(text => !ContainsTheSecret(text));
    }

    [Fact]
    public async Task A_second_delivery_of_the_same_refused_change_fails_the_same_way_and_writes_nothing()
    {
        await Saved(StreamSourceUrl.From(OriginalAddress));
        var savesBefore = repository.SaveCallCount;
        var handler = ChangedHandler();
        var message = Change(FragmentAddress, Moment.AddSeconds(1));

        var first = await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(message, CancellationToken.None));
        var second = await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(message, CancellationToken.None));

        second.Message.ShouldBe(first.Message);
        repository.Streams.Single().SourceUrl.Value.ShouldBe(OriginalAddress);
        repository.SaveCallCount.ShouldBe(savesBefore);
        gateway.RepointCalls.ShouldBeEmpty();
        EveryLoggedText().ShouldAllBe(text => !ContainsTheSecret(text));
    }

    [Fact]
    public async Task A_newer_address_change_whose_host_the_parser_cannot_read_fails_typed_not_as_an_index_error()
    {
        await Saved(StreamSourceUrl.From(OriginalAddress));
        var handler = ChangedHandler();

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            Change(UnreadableHostAddress, Moment.AddSeconds(1)),
            CancellationToken.None));

        failure.Message.ShouldContain("STREAM_INVALID_RTSP_SOURCE");
        repository.Streams.Single().SourceUrl.Value.ShouldBe(OriginalAddress);
        gateway.RepointCalls.ShouldBeEmpty();
        EveryLoggedText().ShouldAllBe(text => !text.Contains("1:2:3:4", StringComparison.Ordinal));
    }

    private static bool ContainsTheSecret(string text) =>
        text.Contains("10.0.0.66", StringComparison.Ordinal)
        || text.Contains("evil", StringComparison.Ordinal);

    private static CameraAddressChangedV1 Change(string url, DateTimeOffset changedAt) =>
        new(CameraGuid, "munich", OriginalAddress, url, changedAt, AnAdmin.Value, Metadata);

    private async Task<Domain.Stream.Stream> Saved(StreamSourceUrl source)
    {
        var stream = new StreamBuilder()
            .ForCamera(CameraIdentifier.From(CameraGuid))
            .WithSourceUrl(source)
            .ProvisionedBy(AnAdmin)
            .At(Moment)
            .Build();
        repository.Add(stream);
        await repository.SaveAsync(CancellationToken.None);

        return stream;
    }

    private CameraRegisteredIntegrationEventHandler RegisteredHandler() => new(
        new ProvisionStreamCommandHandler(repository, gateway, new FixedClock(Moment), provisionLogger),
        registeredLogger);

    private CameraAddressChangedIntegrationEventHandler ChangedHandler() => new(
        new RepointStreamCommandHandler(
            repository,
            gateway,
            new FixedClock(Moment.AddHours(3)),
            repointLogger),
        changedLogger);

    private IEnumerable<string> EveryLoggedText() =>
        provisionLogger.Entries
            .Concat(registeredLogger.Entries)
            .Concat(repointLogger.Entries)
            .Concat(changedLogger.Entries)
            .SelectMany(entry => new[] { entry.Message, entry.Exception?.ToString() ?? string.Empty });
}
