using System.Globalization;
using System.Net;
using SmartSentinelEye.CameraCatalog.Application.Queries;
using SmartSentinelEye.CameraCatalog.Application.Queries.Handlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Queries;

/// <summary>
/// Spec 438 (#3016, ADR-0179) - the writer's full-address read: the audit event is published and committed
/// before the address is handed back, and a refusal publishes nothing.
/// </summary>
public class GetCameraAddressQueryHandlerTests
{
    private const string StoredAddress = "rtsp://10.0.5.98/stream?token=PWMARK";

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-10-22T10:00:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier AnOperator = OperatorIdentifier.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_camera_in_the_callers_fab_returns_its_full_address_and_version()
    {
        var camera = RegisterIn("munich");
        var (handler, _) = NewHandler(false, camera);

        var result = await handler.HandleAsync(QueryFor(camera, "munich"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RtspUrl.ShouldBe(StoredAddress);
        result.Value.Version.ShouldBe(camera.Version);
    }

    [Fact]
    public async Task A_read_publishes_one_event_naming_the_camera_fab_reader_and_moment()
    {
        var camera = RegisterIn("munich");
        var (handler, log) = NewHandler(false, camera);

        await handler.HandleAsync(QueryFor(camera, "munich"), CancellationToken.None);

        var published = log.Published.OfType<CameraAddressReadV1>().ToList();
        published.Count.ShouldBe(1);
        published[0].Camera.ShouldBe(camera.Id.Value);
        published[0].Fab.ShouldBe("munich");
        published[0].ReadBy.ShouldBe(AnOperator.Value);
        published[0].ReadAt.ShouldBe(FixedMoment);
        published[0].Metadata.Actor.ShouldBe(AnOperator.Value);
        published[0].Metadata.Fab.ShouldBe("munich");
    }

    [Fact]
    public async Task The_event_is_published_once_and_committed_once_with_the_publish_first()
    {
        var camera = RegisterIn("munich");
        var (handler, log) = NewHandler(false, camera);

        await handler.HandleAsync(QueryFor(camera, "munich"), CancellationToken.None);

        log.Steps.ShouldBe(["publish", "commit"]);
    }

    [Fact]
    public async Task An_unknown_camera_is_not_found_and_publishes_and_commits_nothing()
    {
        var (handler, log) = NewHandler(false);

        var result = await handler.HandleAsync(
            new GetCameraAddressQuery(
                FabIdentifier.From("munich"),
                CameraIdentifier.From(Guid.CreateVersion7()),
                AnOperator),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<GetCameraAddressError.CameraNotFound>();
        result.Error.Code.ShouldBe("CAMERA_NOT_FOUND");
        result.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        log.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_fabs_camera_is_refused_exactly_as_an_unknown_one_is_and_nothing_is_published()
    {
        var inMunich = RegisterIn("munich");
        var (handler, log) = NewHandler(false, inMunich);

        var crossFab = await handler.HandleAsync(QueryFor(inMunich, "dresden"), CancellationToken.None);
        var neverExisted = await handler.HandleAsync(
            new GetCameraAddressQuery(
                FabIdentifier.From("dresden"),
                CameraIdentifier.From(Guid.CreateVersion7()),
                AnOperator),
            CancellationToken.None);

        crossFab.IsFailure.ShouldBeTrue();
        neverExisted.IsFailure.ShouldBeTrue();
        crossFab.Error.ShouldBeOfType<GetCameraAddressError.CameraNotFound>();
        crossFab.Error.Code.ShouldBe(neverExisted.Error.Code);
        crossFab.Error.Status.ShouldBe(neverExisted.Error.Status);
        log.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_commit_propagates_and_no_address_is_returned()
    {
        var camera = RegisterIn("munich");
        var (handler, _) = NewHandler(true, camera);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(QueryFor(camera, "munich"), CancellationToken.None));

        thrown.Message.ShouldBe("commit failed");
    }

    private static GetCameraAddressQuery QueryFor(Camera camera, string fab) =>
        new(FabIdentifier.From(fab), camera.Id, AnOperator);

    private static (GetCameraAddressQueryHandler Handler, StepLog Log) NewHandler(
        bool commitFails,
        params Camera[] cameras)
    {
        StepLog log = new();
        GetCameraAddressQueryHandler handler = new(
            new InMemoryCameraQuerySource([.. cameras]),
            new OrderingEventBus(log),
            new OrderingCommit(log, commitFails),
            new FixedClock(FixedMoment));

        return (handler, log);
    }

    private static Camera RegisterIn(string fab) =>
        Camera.Register(
            FabIdentifier.From(fab),
            CameraName.From("Dock-2"),
            RtspUrl.From(StoredAddress),
            AnOperator,
            new FixedClock(FixedMoment));

    private sealed class StepLog
    {
        public List<string> Steps { get; } = [];

        public List<object> Published { get; } = [];
    }

    private sealed class OrderingEventBus(StepLog log) : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            log.Steps.Add("publish");
            log.Published.Add(integrationEvent);

            return Task.CompletedTask;
        }
    }

    private sealed class OrderingCommit(StepLog log, bool fails) : ITransactionalCommit
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            if (fails)
            {
                throw new InvalidOperationException("commit failed");
            }

            log.Steps.Add("commit");

            return Task.CompletedTask;
        }
    }
}
