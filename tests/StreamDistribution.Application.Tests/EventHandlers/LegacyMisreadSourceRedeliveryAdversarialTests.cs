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
/// Spec 389 / issue #2999, adversarial (T050). Wolverine redelivers a failed message up to its default three
/// attempts before the error queue; each attempt runs the real event handler over the real command handler here.
/// What must hold on every attempt: the handler throws, nothing is saved, the gateway is not touched and no log
/// line or exception text carries the address. Also pinned: a stream already stored at a now-refused source
/// (read back through <c>FromStored</c>) is not re-asserted by a redelivered registration, because the source is
/// validated before the existing row is looked up, so the reconciler (which reads through <c>FromStored</c>) is
/// what restores such a path; and it can still be re-pointed to a valid address.
/// </summary>
public class LegacyMisreadSourceRedeliveryAdversarialTests
{
    private const int DefaultAttempts = 3;

    private const string MisreadAddress = "rtsp://admin:4242#@cam/s";

    private const string ValidAddress = "rtsp://camera-sim:8554/b";

    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin =
        OperatorIdentifier.From(Guid.Parse("0197a1b2-0000-7000-8000-000000000002", CultureInfo.InvariantCulture));

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

    private readonly List<Exception> thrown = [];

    public static TheoryData<string, string> Refused => new()
    {
        { "rtsp://admin:4242#@cam/s", "4242" },
        { "rtsp://admin:4711?@cam/s", "4711" },
        { "rtsp://admin:8080/x@cam/s", "8080" },
        { "RTSP://ADMIN:12345#@CAM", "12345" },
        { "rtsp://3232235777:12345#@cam", "3232235777" },
        { "RtSp://admin:4242#@cam", "4242" },
        { "rtsp://cam/s?u=admin:4242@x", "4242" },
        { "rtsp://cam/s\\admin:4242@x", "4242" },
        { "rtsp://admin:4242#@[fd00::5]:8554/x", "4242" },
        { "rtsp://admin:4242\\@cam/s", "4242" },
        { "rtsp://admin:4242@cam/s", "4242" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Every_attempt_at_a_refused_registration_fails_and_leaves_nothing_behind(string address, string secret)
    {
        var handler = RegisteredHandler();

        for (var attempt = 0; attempt < DefaultAttempts; attempt++)
        {
            thrown.Add(await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(Registered(address), CancellationToken.None)));
        }

        repository.Streams.ShouldBeEmpty();
        repository.SaveCallCount.ShouldBe(0);
        gateway.AddCalls.ShouldBeEmpty();
        gateway.RemoveCalls.ShouldBeEmpty();
        registeredLogger.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(DefaultAttempts);
        EveryText().ShouldAllBe(text => !text.Contains(secret, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Every_attempt_at_a_refused_address_change_fails_and_the_stream_keeps_its_source(string address, string secret)
    {
        var stream = await Seed(StreamSourceUrl.From(ValidAddress));
        var savesBefore = repository.SaveCallCount;
        var handler = ChangedHandler();

        for (var attempt = 0; attempt < DefaultAttempts; attempt++)
        {
            thrown.Add(await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(Changed(ValidAddress, address, attempt), CancellationToken.None)));
        }

        stream.SourceUrl.Value.ShouldBe(ValidAddress);
        repository.SaveCallCount.ShouldBe(savesBefore);
        gateway.RepointCalls.ShouldBeEmpty();
        gateway.AddCalls.ShouldBeEmpty();
        EveryText().ShouldAllBe(text => !text.Contains(secret, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_redelivered_registration_onto_a_stream_stored_at_the_refused_source_fails_without_reasserting_the_path()
    {
        var stream = await Seed(StreamSourceUrl.FromStored(MisreadAddress));
        var savesBefore = repository.SaveCallCount;
        var handler = RegisteredHandler();

        for (var attempt = 0; attempt < DefaultAttempts; attempt++)
        {
            thrown.Add(await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(Registered(MisreadAddress), CancellationToken.None)));
        }

        repository.Streams.ShouldHaveSingleItem().ShouldBeSameAs(stream);
        stream.SourceUrl.Value.ShouldBe(MisreadAddress);
        repository.SaveCallCount.ShouldBe(savesBefore);
        gateway.AddCalls.ShouldBeEmpty();
        EveryText().ShouldAllBe(text => !text.Contains("4242", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_redelivered_change_to_the_refused_source_a_stream_already_holds_still_fails()
    {
        var stream = await Seed(StreamSourceUrl.FromStored(MisreadAddress));
        var savesBefore = repository.SaveCallCount;

        await Should.ThrowAsync<InvalidOperationException>(() => ChangedHandler().Handle(Changed(ValidAddress, MisreadAddress, 0), CancellationToken.None));

        stream.SourceUrl.Value.ShouldBe(MisreadAddress);
        repository.SaveCallCount.ShouldBe(savesBefore);
        gateway.RepointCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stream_stored_at_the_refused_source_can_still_be_repointed_to_a_valid_one()
    {
        var stream = await Seed(StreamSourceUrl.FromStored(MisreadAddress));

        await ChangedHandler().Handle(Changed(MisreadAddress, ValidAddress, 0), CancellationToken.None);

        stream.SourceUrl.Value.ShouldBe(ValidAddress);
        gateway.RepointCalls.ShouldHaveSingleItem().Source.ShouldBe(ValidAddress);
        EveryText().ShouldAllBe(text => !text.Contains("4242", StringComparison.Ordinal));
    }

    private async Task<Domain.Stream.Stream> Seed(StreamSourceUrl source)
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

    private CameraRegisteredIntegrationEventHandler RegisteredHandler() =>
        new(new ProvisionStreamCommandHandler(repository, gateway, new FixedClock(Moment), provisionLogger), registeredLogger);

    private CameraAddressChangedIntegrationEventHandler ChangedHandler() =>
        new(
            new RepointStreamCommandHandler(
                repository,
                gateway,
                new FixedClock(Moment.AddHours(1)),
                repointLogger),
            changedLogger);

    private static CameraRegisteredV1 Registered(string address) =>
        new(CameraGuid, "Line-1", address, Moment, AnAdmin.Value, Metadata);

    private static CameraAddressChangedV1 Changed(string previous, string address, int attempt) =>
        new(CameraGuid, "munich", previous, address, Moment.AddSeconds(1 + attempt), AnAdmin.Value, Metadata);

    private IEnumerable<string> EveryText() =>
        provisionLogger.Entries
            .Concat(registeredLogger.Entries)
            .Concat(repointLogger.Entries)
            .Concat(changedLogger.Entries)
            .SelectMany(entry => new[] { entry.Message, entry.Exception?.ToString() ?? string.Empty })
            .Concat(thrown.Select(exception => exception.ToString()));
}
