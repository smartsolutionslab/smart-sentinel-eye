using System.Globalization;
using Microsoft.Extensions.Logging;
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
/// Spec 394 / issue #3023 items 1, 2 and 5, red. A <c>CameraRegisteredV1</c> or <c>CameraAddressChangedV1</c>
/// published before the camera URL value objects started refusing '@', '?' and '#' inside the IPv6 brackets (still
/// in the outbox at deploy, or replayed from the error queue) can carry an address the new rule refuses. The real
/// event handler over the real command handler must fail the message with the typed failure, write nothing, call no
/// gateway and log the code but never the address. An address whose host the parser cannot read fails the same way
/// instead of escaping as an <see cref="IndexOutOfRangeException"/>. No failure policy is added (spec 389).
/// </summary>
public class LegacyZoneDelimiterReplayTests
{
    private const string HiddenHostAddress = "rtsp://@[fe80::1%@10.0.0.66:8554]/live";

    private const string QuestionMarkAddress = "rtsp://[::1%25x?y]/s";

    private const string UnreadableHostAddress = "rtsp://[1:2:3:4:5:6:7:8:/64]";

    private const string OriginalAddress = "rtsp://camera-sim:8554/b";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-17T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture));

    private static readonly Guid CameraGuid = Guid.Parse("0197a1b2-0000-7000-8000-0000000000e4", CultureInfo.InvariantCulture);

    private static readonly EventMetadata Metadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000ac", CultureInfo.InvariantCulture),
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
    public async Task A_registration_carrying_a_hidden_host_in_the_zone_fails_and_provisions_nothing()
    {
        var handler = RegisteredHandler();

        await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            new CameraRegisteredV1(CameraGuid, "Line-1", HiddenHostAddress, Moment, AnAdmin.Value, Metadata),
            CancellationToken.None));

        repository.Streams.ShouldBeEmpty();
        gateway.AddCalls.ShouldBeEmpty();
        var warning = registeredLogger.Entries.Where(entry => entry.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain("STREAM_INVALID_RTSP_SOURCE");
        EveryLoggedText().ShouldAllBe(text => !ContainsTheSecret(text));
    }

    [Fact]
    public async Task A_newer_address_change_carrying_a_question_mark_in_the_zone_fails_and_leaves_the_stream_where_it_was()
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
                new ChangedAtPolicy(new FixedClock(Moment.AddHours(1)), Options.Create(new StreamRepointOptions())),
                repointLogger),
            changedLogger);

        await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            new CameraAddressChangedV1(
                CameraGuid, "munich", OriginalAddress, QuestionMarkAddress, Moment.AddSeconds(1), AnAdmin.Value, Metadata),
            CancellationToken.None));

        repository.Streams.Single().SourceUrl.Value.ShouldBe(OriginalAddress);
        repository.SaveCallCount.ShouldBe(savesBefore);
        gateway.RepointCalls.ShouldBeEmpty();
        changedLogger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("STREAM_INVALID_RTSP_SOURCE", StringComparison.Ordinal));
        EveryLoggedText().ShouldAllBe(text => !ContainsTheSecret(text));
    }

    [Fact]
    public async Task A_registration_whose_host_the_parser_cannot_read_fails_with_the_typed_failure_not_an_escaped_index_error()
    {
        var handler = RegisteredHandler();

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            new CameraRegisteredV1(CameraGuid, "Line-1", UnreadableHostAddress, Moment, AnAdmin.Value, Metadata),
            CancellationToken.None));

        failure.ShouldNotBeOfType<IndexOutOfRangeException>();
        repository.Streams.ShouldBeEmpty();
        gateway.AddCalls.ShouldBeEmpty();
        var warning = registeredLogger.Entries.Where(entry => entry.Level == LogLevel.Warning).ShouldHaveSingleItem();
        warning.Message.ShouldContain("STREAM_INVALID_RTSP_SOURCE");
    }

    private static bool ContainsTheSecret(string text) =>
        text.Contains("10.0.0.66", StringComparison.Ordinal)
        || text.Contains("x?y", StringComparison.Ordinal);

    private CameraRegisteredIntegrationEventHandler RegisteredHandler() => new(
        new ProvisionStreamCommandHandler(repository, gateway, new FixedClock(Moment), provisionLogger),
        registeredLogger);

    private IEnumerable<string> EveryLoggedText() =>
        provisionLogger.Entries
            .Concat(registeredLogger.Entries)
            .Concat(repointLogger.Entries)
            .Concat(changedLogger.Entries)
            .SelectMany(entry => new[] { entry.Message, entry.Exception?.ToString() ?? string.Empty });
}
