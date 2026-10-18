using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.CameraCatalog.Application.Commands;
using SmartSentinelEye.CameraCatalog.Application.Commands.Handlers;
using SmartSentinelEye.CameraCatalog.Application.EventHandlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Commands;

/// <summary>
/// Spec 403 (#3008), adversarial pass on the publisher's sequence: a run of PATCHes through the real
/// <see cref="ChangeCameraAddressCommandHandler"/> and the real <see cref="CameraAddressChangedDomainEventHandler"/>,
/// with a repository double that does what the real one does at save (dispatch the pending events, then commit)
/// and what the persistence interceptor does at commit (bump the version of a root that changed).
///
/// <para>
/// Not pinned: the interceptor and the EF concurrency token themselves (<c>AggregateVersionInterceptorTests</c>
/// in ServiceDefaults.Tests and the integration suite own those). The double bumps only when a change raised an
/// event, which is the real interceptor's "Modified root" rule for this aggregate's address change.
/// </para>
/// </summary>
public class ChangeCameraAddressVersionAdversarialTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnAdmin = OperatorIdentifier.From(Guid.CreateVersion7());

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private const string OriginalUrl = "rtsp://10.0.5.12/h264";

    private sealed class CommittingCameraRepository(FakeEventBus bus) : ICameraRepository
    {
        private readonly InMemoryCameraRepository inner = new();

        public Task<Option<Camera>> GetByIdentifierAsync(CameraIdentifier camera, CancellationToken cancellationToken) =>
            inner.GetByIdentifierAsync(camera, cancellationToken);

        public Task<Option<Camera>> GetWithinFabAsync(
            FabIdentifier fab,
            CameraIdentifier camera,
            CancellationToken cancellationToken) =>
            inner.GetWithinFabAsync(fab, camera, cancellationToken);

        public Task<bool> ExistsByNameAsync(
            FabIdentifier fab,
            CameraName name,
            Option<CameraIdentifier> excluding,
            CancellationToken cancellationToken) =>
            inner.ExistsByNameAsync(fab, name, excluding, cancellationToken);

        public void Add(Camera camera) => inner.Add(camera);

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await inner.SaveAsync(cancellationToken);
            CameraAddressChangedDomainEventHandler publisher = new(bus);

            foreach (var camera in inner.Cameras)
            {
                var pending = camera.PendingEvents.ToArray();
                camera.ClearPendingEvents();

                foreach (var change in pending.OfType<CameraAddressChangedDomainEvent>())
                {
                    await publisher.Handle(change, cancellationToken);
                }

                if (pending.OfType<CameraAddressChangedDomainEvent>().Any())
                {
                    SetVersion(camera, camera.Version.Value + 1);
                }
            }
        }

        private static void SetVersion(Camera camera, int version)
        {
            var setter = camera.GetType()
                .GetProperty(nameof(IVersionedAggregate.Version), BindingFlags.Public | BindingFlags.Instance)!
                .GetSetMethod(nonPublic: true)!;

            setter.Invoke(camera, [AggregateVersion.From(version)]);
        }
    }

    [Fact]
    public async Task Every_published_change_carries_the_if_match_it_was_accepted_under_and_the_sequence_strictly_increases()
    {
        FakeEventBus bus = new();
        CommittingCameraRepository cameras = new(bus);
        var camera = await SeedAsync(cameras);
        var handler = NewHandler(cameras);
        List<int> acceptedIfMatch = [];

        for (var change = 1; change <= 25; change++)
        {
            var ifMatch = camera.Version.Value;
            var url = RtspUrl.From($"rtsp://10.0.5.{(change % 2 == 0 ? 44 : 45)}/h264");
            var command = new ChangeCameraAddressCommand(Munich, camera.Id, url, ifMatch, AnAdmin);

            (await handler.HandleAsync(command, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            acceptedIfMatch.Add(ifMatch);
        }

        var published = bus.Published.OfType<CameraAddressChangedV1>().Select(message => message.PreviousVersion).ToList();
        published.ShouldBe([.. acceptedIfMatch.Select(version => (int?)version)]);
        published.ShouldBe([.. published.Distinct().Order()]);
    }

    [Fact]
    public async Task A_no_op_change_publishes_nothing_and_the_next_real_change_carries_the_unchanged_version()
    {
        FakeEventBus bus = new();
        CommittingCameraRepository cameras = new(bus);
        var camera = await SeedAsync(cameras);
        var handler = NewHandler(cameras);

        var noOp = new ChangeCameraAddressCommand(Munich, camera.Id, RtspUrl.From(OriginalUrl), 0, AnAdmin);
        (await handler.HandleAsync(noOp, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        bus.Published.ShouldBeEmpty();
        camera.Version.ShouldBe(AggregateVersion.Initial);

        var real = noOp with { Url = RtspUrl.From("rtsp://10.0.5.44/h264") };
        (await handler.HandleAsync(real, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        bus.Published.OfType<CameraAddressChangedV1>().ShouldHaveSingleItem().PreviousVersion.ShouldBe(0);
    }

    [Fact]
    public async Task A_second_request_with_the_same_if_match_is_refused_and_publishes_no_second_event_for_that_version()
    {
        FakeEventBus bus = new();
        CommittingCameraRepository cameras = new(bus);
        var camera = await SeedAsync(cameras);
        var handler = NewHandler(cameras);

        var first = new ChangeCameraAddressCommand(Munich, camera.Id, RtspUrl.From("rtsp://10.0.5.44/h264"), 0, AnAdmin);
        var racing = first with { Url = RtspUrl.From("rtsp://10.0.5.45/h264") };

        (await handler.HandleAsync(first, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var refused = await handler.HandleAsync(racing, CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        bus.Published.OfType<CameraAddressChangedV1>().ShouldHaveSingleItem().PreviousVersion.ShouldBe(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public async Task An_if_match_that_is_not_the_loaded_version_publishes_nothing(int ifMatch)
    {
        FakeEventBus bus = new();
        CommittingCameraRepository cameras = new(bus);
        var camera = await SeedAsync(cameras);

        var stale = new ChangeCameraAddressCommand(Munich, camera.Id, RtspUrl.From("rtsp://10.0.5.44/h264"), ifMatch, AnAdmin);
        var result = await NewHandler(cameras).HandleAsync(stale, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        bus.Published.ShouldBeEmpty();
    }

    private static async Task<Camera> SeedAsync(CommittingCameraRepository cameras)
    {
        var camera = Camera.Register(
            Munich,
            CameraName.From("line-3-inlet"),
            RtspUrl.From(OriginalUrl),
            AnAdmin,
            new FixedClock(Moment));
        camera.ClearPendingEvents();

        cameras.Add(camera);
        await cameras.SaveAsync(CancellationToken.None);
        return camera;
    }

    private static ChangeCameraAddressCommandHandler NewHandler(CommittingCameraRepository cameras) =>
        new(cameras, new FixedClock(Moment), NullLogger<ChangeCameraAddressCommandHandler>.Instance);
}
